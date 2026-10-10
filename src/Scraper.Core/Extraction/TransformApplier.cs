using Scraper.Core.Interfaces;

namespace Scraper.Core.Extraction;

/// <summary>
/// Applies a field mapping's registered transform to a raw extracted value before type coercion.
/// Shared by all item sources: null values and mappings without a transform pass through unchanged.
/// </summary>
internal static class TransformApplier
{
    public static IReadOnlyDictionary<string, IFieldTransform> Index(IEnumerable<IFieldTransform> transforms) =>
        transforms.ToDictionary(t => t.Name, StringComparer.OrdinalIgnoreCase);

    public static object? Apply(IReadOnlyDictionary<string, IFieldTransform> transforms, string? transformName, object? value)
    {
        if (value is null || transformName is null)
            return value;

        // ConfigLoader already rejects unregistered transform names, so a miss here means the engine
        // and the loader disagree on what is registered.
        if (!transforms.TryGetValue(transformName, out var transform))
            throw new ConfigurationException($"Field transform '{transformName}' is not registered");

        return transform.Transform(value);
    }
}
