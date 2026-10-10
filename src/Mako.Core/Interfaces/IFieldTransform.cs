namespace Mako.Core.Interfaces;

/// <summary>
/// A named value transform applied to an extracted raw field value before type coercion.
/// Registered by name so job configurations select it via a mapping's <c>transform</c>.
/// </summary>
public interface IFieldTransform : INamedCapability
{
    object? Transform(object? value);
}
