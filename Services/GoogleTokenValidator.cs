using System.Threading;
using System.Threading.Tasks;
using Google.Apis.Auth;
using healthspan_api.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Services;

#nullable enable

public class GoogleTokenValidator
{
    private readonly GoogleAuthSettings _settings;
    private readonly ILogger<GoogleTokenValidator> _logger;

    public GoogleTokenValidator(IOptions<GoogleAuthSettings> options, ILogger<GoogleTokenValidator> logger)
    {
        _settings = options.Value;
        _logger = logger;
    }

    public async Task<GoogleUserProfile?> ValidateAsync(string idToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(idToken))
        {
            return null;
        }

        var validationSettings = new GoogleJsonWebSignature.ValidationSettings
        {
            Audience = new[] { _settings.ClientId }
        };

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, validationSettings);
            cancellationToken.ThrowIfCancellationRequested();

            return new GoogleUserProfile(
                payload.Subject,
                payload.Email,
                payload.EmailVerified,
                payload.Name,
                payload.Picture);
        }
        catch (InvalidJwtException ex)
        {
            _logger.LogWarning(ex, "Failed to validate Google ID token.");
            return null;
        }
    }
}

