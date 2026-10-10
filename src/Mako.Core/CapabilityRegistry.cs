using System.Diagnostics.CodeAnalysis;
using Mako.Core.Interfaces;

namespace Mako.Core;

/// <summary>
/// The name → capability lookup every config strategy field resolves through. Built from the
/// capabilities registered in DI (built-ins only in v1); unknown names surface as configuration
/// errors before any network or database access.
/// </summary>
public sealed class CapabilityRegistry(
    IEnumerable<IItemSource> sources,
    IEnumerable<IPaginationStrategy> paginationStrategies,
    IEnumerable<IFieldTransform> transforms)
{
    private readonly Dictionary<string, IItemSource> _sources = Index(sources);
    private readonly Dictionary<string, IPaginationStrategy> _paginationStrategies = Index(paginationStrategies);
    private readonly Dictionary<string, IFieldTransform> _transforms = Index(transforms);

    public bool TryGetSource(string name, [NotNullWhen(true)] out IItemSource? source) =>
        _sources.TryGetValue(name, out source);

    public bool TryGetPaginationStrategy(string name, [NotNullWhen(true)] out IPaginationStrategy? strategy) =>
        _paginationStrategies.TryGetValue(name, out strategy);

    public bool TryGetTransform(string name, [NotNullWhen(true)] out IFieldTransform? transform) =>
        _transforms.TryGetValue(name, out transform);

    public string RegisteredSources => string.Join(", ", _sources.Keys.OrderBy(n => n));

    public string RegisteredPaginationStrategies => string.Join(", ", _paginationStrategies.Keys.OrderBy(n => n));

    public string RegisteredTransforms => string.Join(", ", _transforms.Keys.OrderBy(n => n));

    private static Dictionary<string, T> Index<T>(IEnumerable<T> items) where T : class, INamedCapability
    {
        var map = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in items)
            map[item.Name] = item;
        return map;
    }
}
