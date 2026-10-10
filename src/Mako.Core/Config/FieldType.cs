namespace Mako.Core.Config;

/// <summary>The supported field types a mapping can coerce its value to.</summary>
public enum FieldType
{
    Text,
    Integer,
    Long,
    Real,
    Date,
}

public static class FieldTypeParser
{
    private static readonly Dictionary<string, FieldType> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["text"] = FieldType.Text,
        ["integer"] = FieldType.Integer,
        ["long"] = FieldType.Long,
        ["real"] = FieldType.Real,
        ["date"] = FieldType.Date,
    };

    public static bool TryParse(string? name, out FieldType type) =>
        Names.TryGetValue(name ?? string.Empty, out type);

    public static string SupportedNames { get; } = "text | integer | long | real | date";
}
