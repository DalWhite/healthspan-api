using Microsoft.Extensions.Configuration;

namespace healthspan_api.Options;

/// <summary>
/// Gate for the QA seed account in non-production deployments (dev API only).
/// Default is DISABLED: an environment only gets the seed account if its
/// deploy pipeline explicitly sets DevQaSeed:Enabled=true. Production keeps
/// the committed default (false), so the QA password account can never be
/// activated there by configuration drift.
/// </summary>
public class DevQaSeedSettings
{
    public const string SectionName = "DevQaSeed";

    public bool Enabled { get; set; }

    [ConfigurationKeyName("Email")]
    public string Email { get; set; } = "qa@dev.local";

    [ConfigurationKeyName("Password")]
    public string Password { get; set; } = string.Empty;
}
