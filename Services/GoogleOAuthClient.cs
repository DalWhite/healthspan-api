using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using healthspan_api.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Services;

#nullable enable

public class GoogleOAuthClient
{
    private static readonly Uri TokenRevocationEndpoint = new("https://oauth2.googleapis.com/revoke");
    private readonly HttpClient _httpClient;
    private readonly GoogleAuthSettings _settings;
    private readonly ILogger<GoogleOAuthClient> _logger;

    public GoogleOAuthClient(HttpClient httpClient, IOptions<GoogleAuthSettings> options, ILogger<GoogleOAuthClient> logger)
    {
        _httpClient = httpClient;
        _settings = options.Value;
        _logger = logger;
    }

    public Task<bool> RevokeRefreshTokenAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
        {
            return Task.FromResult(false);
        }

        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["token"] = refreshToken,
            ["client_id"] = _settings.ClientId,
            ["client_secret"] = _settings.ClientSecret
        });

        return SendRevocationAsync(content, cancellationToken);
    }

    private async Task<bool> SendRevocationAsync(FormUrlEncodedContent content, CancellationToken cancellationToken)
    {
        using var response = await _httpClient.PostAsync(TokenRevocationEndpoint, content, cancellationToken);

        if (response.IsSuccessStatusCode)
        {
            return true;
        }

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning("Failed to revoke Google refresh token. Status: {Status}. Body: {Body}", response.StatusCode, body);
        return false;
    }
}

