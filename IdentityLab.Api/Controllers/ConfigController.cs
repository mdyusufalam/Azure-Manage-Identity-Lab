using Microsoft.AspNetCore.Mvc;

namespace IdentityLab.Api.Controllers;

[ApiController]
[Route("api/config")]
public sealed class ConfigController : ControllerBase
{
    private readonly IConfiguration _config;

    public ConfigController(IConfiguration config) => _config = config;

    /// <summary>
    /// Proves both wiring paths work:
    ///   - <c>Demo:Message</c> is a plain key stored in Azure App Configuration.
    ///   - <c>Demo:SecretValue</c> is stored in App Configuration as a Key Vault
    ///     *reference*; it was resolved to the real secret at startup by
    ///     ConfigureKeyVault, using the same managed identity.
    /// Both arrive here as ordinary <see cref="IConfiguration"/> values.
    /// </summary>
    [HttpGet("demo")]
    public IActionResult Demo()
    {
        var fromAppConfig = _config["Demo:Message"];
        var fromKeyVault = _config["Demo:SecretValue"];

        if (fromAppConfig is null && fromKeyVault is null)
        {
            return Problem(
                statusCode: StatusCodes.Status503ServiceUnavailable,
                title: "App Configuration is not wired up",
                detail: "Neither 'Demo:Message' nor 'Demo:SecretValue' resolved. " +
                        "Check 'Azure:AppConfig:Endpoint', the role assignment, and that the keys exist.");
        }

        return Ok(new
        {
            fromAppConfiguration = fromAppConfig,
            fromKeyVault = Mask(fromKeyVault),
        });
    }

    /// <summary>Shows only the first three characters of a secret, e.g. "abc**********".</summary>
    private static string? Mask(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return value;
        }

        return value.Length <= 3
            ? new string('*', value.Length)
            : string.Concat(value.AsSpan(0, 3), new string('*', value.Length - 3));
    }
}
