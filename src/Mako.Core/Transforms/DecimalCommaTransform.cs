using Mako.Core.Interfaces;

namespace Mako.Core.Transforms;

/// <summary>
/// Replaces decimal commas with points so values like "1,43" coerce to real or integer types.
/// Non-string values pass through unchanged.
/// </summary>
public sealed class DecimalCommaTransform : IFieldTransform
{
    public string Name => "decimal-comma";

    public object? Transform(object? value) => value is string s ? s.Replace(',', '.') : value;
}
