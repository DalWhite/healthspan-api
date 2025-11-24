using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace Services;

#nullable enable

public class GoogleRefreshTokenProtector : IGoogleRefreshTokenProtector
{
    private readonly IDataProtector _dataProtector;

    public GoogleRefreshTokenProtector(IDataProtectionProvider dataProtectionProvider)
    {
        _dataProtector = dataProtectionProvider.CreateProtector("google-refresh-token");
    }

    public string? Protect(string? rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken))
        {
            return null;
        }

        return _dataProtector.Protect(rawToken);
    }

    public string? Unprotect(string? protectedToken)
    {
        if (string.IsNullOrWhiteSpace(protectedToken))
        {
            return null;
        }

        try
        {
            return _dataProtector.Unprotect(protectedToken);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}

