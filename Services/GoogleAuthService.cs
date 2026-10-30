using System;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace Services;

#nullable enable

public class GoogleAuthService
{
    private readonly UserManager<User> _userManager;
    private readonly IGoogleRefreshTokenProtector _refreshTokenProtector;
    private readonly Microsoft.Extensions.Options.IOptions<healthspan_api.Options.RegistrationSettings> _registrationSettings;
    private readonly ILogger<GoogleAuthService> _logger;

    public GoogleAuthService(
        UserManager<User> userManager,
        IGoogleRefreshTokenProtector refreshTokenProtector,
        Microsoft.Extensions.Options.IOptions<healthspan_api.Options.RegistrationSettings> registrationSettings,
        ILogger<GoogleAuthService> logger)
    {
        _userManager = userManager;
        _refreshTokenProtector = refreshTokenProtector;
        _registrationSettings = registrationSettings;
        _logger = logger;
    }

    /// <summary>
    /// Returns the (possibly newly created) user, or null when the account does
    /// not exist and the registration gate (t_3798e8dd) refuses to create it —
    /// callers map that to a 403 registration_closed response.
    /// </summary>
    public async Task<User?> UpsertUserAsync(GoogleUserProfile profile, string? refreshToken, CancellationToken cancellationToken)
    {
        if (profile is null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        var user = await ResolveUserAsync(profile, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var requiresUpdate = false;

        if (!string.IsNullOrWhiteSpace(profile.Name) && user.Name != profile.Name)
        {
            user.Name = profile.Name;
            requiresUpdate = true;
        }

        if (!string.IsNullOrWhiteSpace(profile.PictureUrl) && user.PictureUrl != profile.PictureUrl)
        {
            user.PictureUrl = profile.PictureUrl;
            requiresUpdate = true;
        }

        if (!string.IsNullOrWhiteSpace(profile.Email) && user.Email != profile.Email)
        {
            user.Email = profile.Email;
            requiresUpdate = true;
        }

        if (profile.EmailVerified && !user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            requiresUpdate = true;
        }

        if (user.GoogleSubject != profile.Subject)
        {
            user.GoogleSubject = profile.Subject;
            requiresUpdate = true;
        }

        if (requiresUpdate)
        {
        await EnsureSuccessAsync(_userManager.UpdateAsync(user), "update user profile");
        }

        await EnsureLoginAsync(user, profile.Subject);

        if (!string.IsNullOrWhiteSpace(refreshToken))
        {
            var protectedToken = _refreshTokenProtector.Protect(refreshToken);

            if (user.GoogleRefreshToken != protectedToken)
            {
                user.GoogleRefreshToken = protectedToken;
                await EnsureSuccessAsync(_userManager.UpdateAsync(user), "persist Google refresh token");
            }
        }

        return user;
    }

    public GoogleUserProfile CreateProfileFromPrincipal(ClaimsPrincipal principal)
    {
        var subject = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? principal.FindFirstValue("sub")
                      ?? throw new InvalidOperationException("Missing Google subject claim.");

        var email = principal.FindFirstValue(ClaimTypes.Email);
        var name = principal.FindFirstValue(ClaimTypes.Name) ?? email;
        var picture = principal.FindFirstValue("picture");
        var emailVerifiedClaim = principal.FindFirst("email_verified");
        var emailVerified = emailVerifiedClaim != null &&
                            bool.TryParse(emailVerifiedClaim.Value, out var parsed) &&
                            parsed;

        return new GoogleUserProfile(subject, email, emailVerified, name, picture);
    }

    private async Task<User?> ResolveUserAsync(GoogleUserProfile profile, CancellationToken cancellationToken)
    {
        User? user = null;

        if (!string.IsNullOrWhiteSpace(profile.Subject))
        {
            user = await _userManager.FindByLoginAsync(GoogleDefaults.AuthenticationScheme, profile.Subject);
        }

        if (user is null && !string.IsNullOrWhiteSpace(profile.Email))
        {
            user = await _userManager.FindByEmailAsync(profile.Email);
        }

        if (user is not null)
        {
            return user;
        }

        // Registration gate (t_3798e8dd): never create accounts outside the
        // allowlist via the Google exchange/callback paths.
        if (!_registrationSettings.Value.IsNewAccountAllowed(profile.Email))
        {
            _logger.LogWarning("Google registration denied for non-allowlisted email {Email}", profile.Email);
            return null;
        }

        user = new User
        {
            UserName = profile.Email ?? $"google_{profile.Subject}",
            Email = profile.Email,
            EmailConfirmed = profile.EmailVerified,
            Name = profile.Name ?? profile.Email ?? "Google User",
            PictureUrl = profile.PictureUrl,
            GoogleSubject = profile.Subject
        };

        await EnsureSuccessAsync(_userManager.CreateAsync(user), "create user");

        return user;
    }

    private async Task EnsureLoginAsync(User user, string subject)
    {
        var logins = await _userManager.GetLoginsAsync(user);

        if (logins.Any(login => login.LoginProvider == GoogleDefaults.AuthenticationScheme && login.ProviderKey == subject))
        {
            return;
        }

        await EnsureSuccessAsync(_userManager.AddLoginAsync(
            user,
            new UserLoginInfo(GoogleDefaults.AuthenticationScheme, subject, "Google")),
            "link Google login");
    }

    private async Task EnsureSuccessAsync(Task<IdentityResult> operation, string action)
    {
        var result = await operation;
        if (result.Succeeded)
        {
            return;
        }

        var errors = string.Join(", ", result.Errors.Select(error => error.Description));
        _logger.LogError("Failed to {Action}: {Errors}", action, errors);
        throw new InvalidOperationException($"Unable to {action}: {errors}");
    }
}

