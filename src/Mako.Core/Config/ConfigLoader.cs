using AngleSharp.Html.Parser;
using Mako.Core.Extraction;
using YamlDotNet.Core;
using YamlDotNet.Serialization;

namespace Mako.Core.Config;

/// <summary>
/// Loads and validates a job configuration file. Validation is complete before any network or
/// database access: every error names the offending element, and all errors are reported together.
/// </summary>
public static class ConfigLoader
{
    private static readonly HtmlParser SelectorProbe = new();

    private static readonly System.Text.RegularExpressions.Regex EnvVarPattern =
        new(@"\$\{([A-Za-z_][A-Za-z0-9_]*)\}", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static SiteJob Load(string path, CapabilityRegistry capabilities)
    {
        if (!File.Exists(path))
            throw new ConfigurationException($"Configuration file not found: {path}");

        JobYaml yaml;
        try
        {
            string text = File.ReadAllText(path);
            // YAML keys are lowercase; DTO properties are PascalCase. Unknown (misspelled) keys
            // still fail: YamlDotNet throws PropertyNotFoundException, wrapped below.
            var deserializer = new DeserializerBuilder().WithCaseInsensitivePropertyMatching().Build();
            yaml = deserializer.Deserialize<JobYaml>(text)
                   ?? throw new YamlException("The configuration file is empty.");
        }
        catch (ConfigurationException)
        {
            throw;
        }
        catch (Exception ex) when (ex is YamlException or IOException)
        {
            throw new ConfigurationException($"'{path}' could not be read as a job configuration: {ex.Message}");
        }

        var errors = new List<string>();

        // --- name / url / table ---------------------------------------------------------------
        if (string.IsNullOrWhiteSpace(yaml.Name))
            errors.Add("'name' is required and must be a non-empty string");

        Uri? url = null;
        if (string.IsNullOrWhiteSpace(yaml.Url))
            errors.Add("'url' is required (absolute http(s) start URL)");
        else if (!Uri.TryCreate(yaml.Url, UriKind.Absolute, out var parsedUrl) || parsedUrl.Scheme is not ("http" or "https"))
            errors.Add($"'url' value '{yaml.Url}' is not an absolute http(s) URL");
        else
            url = parsedUrl;

        if (string.IsNullOrWhiteSpace(yaml.Table))
            errors.Add("'table' is required (target table name)");
        else if (!DocumentStore.IsValidTableName(yaml.Table!))
            errors.Add($"'table' value '{yaml.Table}' is not a valid identifier: use a lowercase letter followed by lowercase letters, digits, or underscores");

        // --- source ---------------------------------------------------------------------------
        string? kind = null;
        bool isEmbeddedJson = false;
        var fields = new List<FieldMapping>();
        if (yaml.Source is null)
        {
            errors.Add("'source' is required (kind + items selector/path)");
        }
        else
        {
            kind = yaml.Source.Kind;
            if (string.IsNullOrWhiteSpace(kind))
                errors.Add("'source.kind' is required ('dom' or 'embedded-json')");
            else if (!capabilities.TryGetSource(kind!, out _))
                errors.Add($"source kind '{kind}' is not registered (registered: {capabilities.RegisteredSources})");

            // Only registered kinds reach here (checked above), so literal comparison suffices.
            bool isDom = string.Equals(kind, "dom", StringComparison.OrdinalIgnoreCase);
            isEmbeddedJson = string.Equals(kind, "embedded-json", StringComparison.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(yaml.Source.Items))
                errors.Add(isDom
                    ? "'source.items' is required for 'dom' sources (CSS selector of each item element)"
                    : "'source.items' is required (JSON path selecting the item array)");
            else if (isDom && !IsValidCssSelector(yaml.Source.Items!))
                errors.Add($"'source.items' value '{yaml.Source.Items}' is not a valid CSS selector");

            if (isEmbeddedJson)
            {
                if (string.IsNullOrWhiteSpace(yaml.Source.Marker))
                    errors.Add("'source.marker' is required for 'embedded-json' sources (the exact script-tag prefix holding the payload)");
                if (string.IsNullOrWhiteSpace(yaml.Source.Anchor))
                    errors.Add("'source.anchor' is required for 'embedded-json' sources (JSON path selecting the page-data object)");
                else
                    CheckJsonPath("'source.anchor'", yaml.Source.Anchor!, errors);

                CheckJsonPath("'source.items'", yaml.Source.Items!, errors);
            }
        }

        // --- fields ---------------------------------------------------------------------------
        var declaredNames = new HashSet<string>(StringComparer.Ordinal);
        if (yaml.Fields is null || yaml.Fields.Count == 0)
        {
            errors.Add("'fields' must declare at least one field mapping");
        }
        else
        {
            foreach (var f in yaml.Fields)
            {
                string label = $"field '{(string.IsNullOrWhiteSpace(f.Name) ? "?" : f.Name)}'";

                if (string.IsNullOrWhiteSpace(f.Name))
                    errors.Add($"{label}: 'name' is required");
                else if (!declaredNames.Add(f.Name!))
                    errors.Add($"{label}: field names must be unique");

                if (string.IsNullOrWhiteSpace(f.Source))
                    errors.Add($"{label}: 'source' is required (CSS sub-selector for 'dom', item-relative JSON path for 'embedded-json')");
                else if (kind is not null && string.Equals(kind, "dom", StringComparison.OrdinalIgnoreCase) && !IsValidCssSelector(f.Source!))
                    errors.Add($"{label}: source '{f.Source}' is not a valid CSS selector");
                else if (isEmbeddedJson)
                    CheckJsonPath($"{label} source", f.Source!, errors);

                FieldType type = FieldType.Text;
                if (string.IsNullOrWhiteSpace(f.Type))
                    errors.Add($"{label}: 'type' is required");
                else if (!FieldTypeParser.TryParse(f.Type, out var parsedType))
                    errors.Add($"{label}: type '{f.Type}' is not supported (supported: {FieldTypeParser.SupportedNames})");
                else
                    type = parsedType;

                string? transform = f.Transform;
                if (!string.IsNullOrWhiteSpace(transform) && !capabilities.TryGetTransform(transform!, out _))
                    errors.Add($"{label}: transform '{transform}' is not registered (registered: {capabilities.RegisteredTransforms})");

                fields.Add(new FieldMapping(f.Name ?? "", f.Source ?? "", type, transform, f.Default, f.List));
            }
        }

        // --- key ------------------------------------------------------------------------------
        if (yaml.Key is null || yaml.Key.Count == 0)
        {
            errors.Add("'key' must declare at least one natural-key field");
        }
        else
        {
            foreach (var keyField in yaml.Key)
                if (!declaredNames.Contains(keyField))
                    errors.Add($"key references '{keyField}' which is not a declared field name");

            foreach (var field in fields)
                if (field.List && yaml.Key!.Contains(field.Name, StringComparer.Ordinal))
                    errors.Add($"key field '{field.Name}' cannot be marked as a list field");
        }

        // --- pagination -----------------------------------------------------------------------
        PaginationConfig? pagination = null;
        if (yaml.Pagination is not null)
        {
            var p = yaml.Pagination;
            if (string.IsNullOrWhiteSpace(p.Strategy))
                errors.Add("'pagination.strategy' is required when pagination is declared");
            else if (!capabilities.TryGetPaginationStrategy(p.Strategy!, out _))
                errors.Add($"pagination strategy '{p.Strategy}' is not registered (registered: {capabilities.RegisteredPaginationStrategies})");
            else if (string.Equals(p.Strategy, "next-link", StringComparison.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(p.Selector))
                    errors.Add("'pagination.selector' is required for the 'next-link' strategy");
                else if (!IsValidCssSelector(p.Selector!))
                    errors.Add($"pagination selector '{p.Selector}' is not a valid CSS selector");
            }
            else if (string.Equals(p.Strategy, "page-param", StringComparison.OrdinalIgnoreCase))
            {
                // Page numbers are read from the embedded payload, which only 'embedded-json' sources produce.
                if (!isEmbeddedJson)
                    errors.Add("the 'page-param' strategy requires an 'embedded-json' source (it reads page numbers from the embedded payload)");

                if (string.IsNullOrWhiteSpace(p.Param))
                    errors.Add("'pagination.param' is required for the 'page-param' strategy (URL query parameter name)");
                if (string.IsNullOrWhiteSpace(p.Current))
                    errors.Add("'pagination.current' is required for the 'page-param' strategy (JSON path to the current page number)");
                else
                    CheckJsonPath("'pagination.current'", p.Current!, errors);
                if (string.IsNullOrWhiteSpace(p.Last))
                    errors.Add("'pagination.last' is required for the 'page-param' strategy (JSON path to the last page number)");
                else
                    CheckJsonPath("'pagination.last'", p.Last!, errors);
            }

            pagination = new PaginationConfig(p.Strategy ?? "", p.Selector, p.Param, p.Current, p.Last);
        }

        // --- request options ------------------------------------------------------------------
        string? cookie = yaml.Cookie;
        if (!string.IsNullOrEmpty(cookie))
        {
            foreach (System.Text.RegularExpressions.Match match in EnvVarPattern.Matches(cookie!))
            {
                string variableName = match.Groups[1].Value;
                if (Environment.GetEnvironmentVariable(variableName) is null)
                    errors.Add($"cookie references environment variable '{variableName}' which is not set");
            }

            cookie = EnvVarPattern.Replace(cookie!, match => Environment.GetEnvironmentVariable(match.Groups[1].Value) ?? "");
        }

        if (errors.Count > 0)
            throw new ConfigurationException(
                $"Invalid configuration in {path}:\n" + string.Join("\n", errors.Select(e => $"  - {e}")));

        return new SiteJob(
            yaml.Name!.Trim(),
            url!,
            yaml.Table!.Trim(),
            new SourceConfig(kind!, yaml.Source!.Items!.Trim(), yaml.Source.Marker, yaml.Source.Anchor),
            fields,
            yaml.Key!,
            pagination,
            yaml.Headers,
            cookie);
    }

    /// <summary>Parses a JSON path so invalid syntax fails at load time. Presence is checked separately.</summary>
    private static void CheckJsonPath(string what, string path, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        try
        {
            _ = JsonPaths.Parse(path, what);
        }
        catch (ConfigurationException ex)
        {
            errors.Add(ex.Message);
        }
    }

    /// <summary>Probes a CSS selector against an empty document; AngleSharp rejects invalid syntax.</summary>
    private static bool IsValidCssSelector(string selector)
    {
        try
        {
            var doc = SelectorProbe.ParseDocument("<!doctype html><html><body></body></html>");
            _ = doc.QuerySelector(selector);
            return true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return false;
        }
    }

    // --- YAML shape (matches the configuration file keys exactly) -----------------------------

    private sealed class JobYaml
    {
        public string? Name { get; set; }
        public string? Url { get; set; }
        public string? Table { get; set; }
        public SourceYaml? Source { get; set; }
        public List<FieldYaml>? Fields { get; set; }
        public List<string>? Key { get; set; }
        public PaginationYaml? Pagination { get; set; }
        public Dictionary<string, string>? Headers { get; set; }
        public string? Cookie { get; set; }
    }

    private sealed class SourceYaml
    {
        public string? Kind { get; set; }
        public string? Items { get; set; }
        public string? Marker { get; set; }
        public string? Anchor { get; set; }
    }

    private sealed class FieldYaml
    {
        public string? Name { get; set; }
        public string? Source { get; set; }
        public string? Type { get; set; }
        public string? Transform { get; set; }
        public object? Default { get; set; }
        public bool List { get; set; }
    }

    private sealed class PaginationYaml
    {
        public string? Strategy { get; set; }
        public string? Selector { get; set; }
        public string? Param { get; set; }
        public string? Current { get; set; }
        public string? Last { get; set; }
    }
}
