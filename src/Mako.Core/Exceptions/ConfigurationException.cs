namespace Mako.Core.Exceptions;

/// <summary>Raised when a job configuration file is missing, unparseable, or invalid. Carries all validation errors.</summary>
public sealed class ConfigurationException : Exception
{
    public ConfigurationException(string message) : base(message)
    {
    }
}
