namespace Scraper.Core.Interfaces;

/// <summary>A capability that job configurations select by name.</summary>
public interface INamedCapability
{
    string Name { get; }
}
