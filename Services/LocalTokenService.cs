using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.BearerToken;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;

namespace Services;

#nullable enable

public class LocalTokenService
{
    private readonly SignInManager<User> _signInManager;

    public LocalTokenService(SignInManager<User> signInManager)
    {
        _signInManager = signInManager;
    }

    public async Task<AccessTokenResponse?> IssueTokensAsync(HttpContext httpContext, User user, bool captureResponse, CancellationToken cancellationToken)
    {
        var principal = await _signInManager.CreateUserPrincipalAsync(user);

        if (!captureResponse)
        {
            await httpContext.SignInAsync(IdentityConstants.BearerScheme, principal);
            return null;
        }

        var originalBody = httpContext.Response.Body;
        await using var memoryStream = new MemoryStream();
        httpContext.Response.Body = memoryStream;

        try
        {
            await httpContext.SignInAsync(IdentityConstants.BearerScheme, principal);
            await memoryStream.FlushAsync(cancellationToken);
            memoryStream.Seek(0, SeekOrigin.Begin);

            var tokenResponse = await JsonSerializer.DeserializeAsync<AccessTokenResponse>(
                memoryStream,
                new JsonSerializerOptions(JsonSerializerDefaults.Web),
                cancellationToken: cancellationToken);

            return tokenResponse ?? throw new InvalidOperationException("Failed to deserialize token response.");
        }
        finally
        {
            httpContext.Response.Body = originalBody;
            httpContext.Response.Headers.Clear();
            httpContext.Response.StatusCode = StatusCodes.Status200OK;
            httpContext.Response.ContentLength = null;
            httpContext.Response.ContentType = null;
        }
    }
}

