using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Controllers.ApiContracts.Auth;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Logging;
using Services;

#nullable enable

[ApiController]
[AllowAnonymous]
[Route("auth/google")]
public class GoogleAuthController : ControllerBase
{
    private readonly GoogleTokenValidator _tokenValidator;
    private readonly GoogleAuthService _googleAuthService;
    private readonly LocalTokenService _localTokenService;
    private readonly GoogleReturnUrlResolver _returnUrlResolver;
    private readonly ILogger<GoogleAuthController> _logger;

    public GoogleAuthController(
        GoogleTokenValidator tokenValidator,
        GoogleAuthService googleAuthService,
        LocalTokenService localTokenService,
        GoogleReturnUrlResolver returnUrlResolver,
        ILogger<GoogleAuthController> logger)
    {
        _tokenValidator = tokenValidator;
        _googleAuthService = googleAuthService;
        _localTokenService = localTokenService;
        _returnUrlResolver = returnUrlResolver;
        _logger = logger;
    }

    [HttpPost("exchange")]
    public async Task<IResult> ExchangeIdToken([FromBody] GoogleTokenExchangeRequest request, CancellationToken cancellationToken)
    {
        var profile = await _tokenValidator.ValidateAsync(request.IdToken, cancellationToken);

        if (profile is null)
        {
            return Results.BadRequest(new { error = "Invalid Google id token." });
        }

        var user = await _googleAuthService.UpsertUserAsync(profile, refreshToken: null, cancellationToken);
        await _localTokenService.IssueTokensAsync(HttpContext, user, captureResponse: false, cancellationToken);

        return TypedResults.Empty;
    }

    [HttpGet("challenge")]
    public IActionResult Challenge([FromQuery] string? returnUrl)
    {
        var sanitizedReturnUrl = _returnUrlResolver.Resolve(returnUrl);
        var redirectUri = Url.Action(nameof(Callback)) ?? "/auth/google/callback";
        var properties = new AuthenticationProperties
        {
            RedirectUri = redirectUri,
            Items =
            {
                { "returnUrl", sanitizedReturnUrl }
            }
        };

        return Challenge(properties, GoogleDefaults.AuthenticationScheme);
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback([FromQuery] string? returnUrl, CancellationToken cancellationToken)
    {
        var externalInfo = await HttpContext.AuthenticateAsync(IdentityConstants.ExternalScheme);
        if (!externalInfo.Succeeded || externalInfo.Principal is null)
        {
            _logger.LogWarning("Google callback failed: {Failure}", externalInfo.Failure?.Message ?? "Unknown error");
            return BadRequest("Unable to complete Google authentication.");
        }

        var properties = externalInfo.Properties ?? new AuthenticationProperties();
        properties.Items.TryGetValue("returnUrl", out var storedReturnUrl);
        var resolvedReturnUrl = _returnUrlResolver.Resolve(returnUrl ?? storedReturnUrl);
        var profile = _googleAuthService.CreateProfileFromPrincipal(externalInfo.Principal);
        var refreshToken = properties.GetTokenValue("refresh_token");

        var user = await _googleAuthService.UpsertUserAsync(profile, refreshToken, cancellationToken);
        var tokenResponse = await _localTokenService.IssueTokensAsync(HttpContext, user, captureResponse: true, cancellationToken);

        await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);

        if (tokenResponse is null)
        {
            return BadRequest("Unable to issue local tokens.");
        }

        var redirectUri = QueryHelpers.AddQueryString(
            resolvedReturnUrl,
            new Dictionary<string, string?>
            {
                ["tokenType"] = tokenResponse.TokenType,
                ["accessToken"] = tokenResponse.AccessToken,
                ["refreshToken"] = tokenResponse.RefreshToken,
                ["expiresIn"] = tokenResponse.ExpiresIn.ToString(CultureInfo.InvariantCulture)
            });

        return Redirect(redirectUri);
    }
}

