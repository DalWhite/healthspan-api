using System;
using System.Collections.Generic;
using System.Linq;

namespace healthspan_api.Options;

#nullable enable

/// <summary>
/// Registration gate (t_3798e8dd): the deployed instance must not accept
/// third-party signups. Fail-safe by design: registration is ONLY open when
/// Auth:RegistrationMode is explicitly "Open"; anything else (missing key,
/// typo, empty) means Allowlist mode, and Allowlist mode with an empty
/// AllowedAccounts rejects every new account.
/// </summary>
public class RegistrationSettings
{
    public const string SectionName = "Auth";
    public const string OpenMode = "Open";

    /// <summary>"Allowlist" (default) or "Open".</summary>
    public string? RegistrationMode { get; set; }

    /// <summary>Emails allowed to create a new account in Allowlist mode.</summary>
    public List<string> AllowedAccounts { get; set; } = new();

    public bool IsAllowlistMode =>
        !string.Equals(RegistrationMode, OpenMode, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// True when a NEW account may be created for this email. Existing accounts
    /// are never affected (login/refresh are not gated).
    /// </summary>
    public bool IsNewAccountAllowed(string? email)
    {
        if (!IsAllowlistMode)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        var normalized = email.Trim();
        return AllowedAccounts.Any(
            allowed => !string.IsNullOrWhiteSpace(allowed) &&
                       string.Equals(allowed.Trim(), normalized, StringComparison.OrdinalIgnoreCase));
    }
}
