namespace IdentityLab.Api.Infrastructure;

/// <summary>
/// Thrown by the Azure client factories in <c>Program.cs</c> when a required
/// endpoint / account-name setting is absent. Surfaced as HTTP 503 by
/// <see cref="ProblemExceptionFilter"/> so a misconfigured demo gets a clear
/// message instead of an unhandled 500.
/// </summary>
public sealed class ConfigurationMissingException(string configKey)
    : Exception($"Required configuration value '{configKey}' is not set.")
{
    public string ConfigKey { get; } = configKey;
}
