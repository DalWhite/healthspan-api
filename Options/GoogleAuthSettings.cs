using System.Collections.Generic;
using Microsoft.Extensions.Configuration;

namespace healthspan_api.Options;

public class GoogleAuthSettings
{
    [ConfigurationKeyName("GoogleClientId")]
    public string ClientId { get; set; } = string.Empty;

    [ConfigurationKeyName("GoogleClientSecret")]
    public string ClientSecret { get; set; } = string.Empty;

    [ConfigurationKeyName("GoogleCallbackUri")]
    public string CallbackUri { get; set; } = string.Empty;

    [ConfigurationKeyName("GoogleDefaultReturnUrl")]
    public string DefaultReturnUrl { get; set; } = string.Empty;

    [ConfigurationKeyName("GoogleAllowedReturnUrls")]
    public List<string> AllowedReturnUrls { get; set; } = new();
}

