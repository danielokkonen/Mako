using System.Text.RegularExpressions;
using Mako.Core.Interfaces;

namespace Mako.Core.Transforms;

/// <summary>
/// Collapses every run of whitespace (spaces, tabs, newlines) to a single space and trims the ends,
/// so multi-line cell text becomes one clean line. Non-string values pass through unchanged.
/// </summary>
public sealed class NormalizeWhitespaceTransform : IFieldTransform
{
    private static readonly Regex WhitespaceRun = new(@"\s+", RegexOptions.Compiled);

    public string Name => "normalize-whitespace";

    public object? Transform(object? value) => value is string s ? WhitespaceRun.Replace(s, " ").Trim() : value;
}
