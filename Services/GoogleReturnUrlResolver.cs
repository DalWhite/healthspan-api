using System;
using System.Linq;
using healthspan_api.Options;
using Microsoft.Extensions.Options;

namespace Services;

#nullable enable

public class GoogleReturnUrlResolver
{
    private readonly GoogleAuthSettings _settings;

    public GoogleReturnUrlResolver(IOptions<GoogleAuthSettings> options)
    {
        _settings = options.Value;
    }

    public string Resolve(string? candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate) &&
            Uri.TryCreate(candidate, UriKind.Absolute, out var parsed) &&
            IsAllowed(parsed))
        {
            return parsed.ToString();
        }

        if (!string.IsNullOrWhiteSpace(candidate) &&
            Uri.TryCreate(candidate, UriKind.Relative, out var relative) &&
            Uri.TryCreate(DefaultReturnUrl, UriKind.Absolute, out var defaultUri))
        {
            return new Uri(defaultUri, relative).ToString();
        }

        return DefaultReturnUrl;
    }

    private string DefaultReturnUrl =>
        !string.IsNullOrWhiteSpace(_settings.DefaultReturnUrl)
            ? _settings.DefaultReturnUrl
            : _settings.AllowedReturnUrls.FirstOrDefault()
              ?? throw new InvalidOperationException("At least one allowed return URL must be configured.");

    private bool IsAllowed(Uri uri)
    {
        foreach (var allowed in _settings.AllowedReturnUrls)
        {
            if (Uri.TryCreate(allowed, UriKind.Absolute, out var allowedUri) &&
                string.Equals(uri.Scheme, allowedUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(uri.Host, allowedUri.Host, StringComparison.OrdinalIgnoreCase) &&
                uri.Port == allowedUri.Port &&
                uri.AbsolutePath.StartsWith(allowedUri.AbsolutePath, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

