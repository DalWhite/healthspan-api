using System.Collections.Generic;

namespace healthspan_api.Options;

public class GoogleAuthSettings
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string CallbackUri { get; set; } = string.Empty;
    public string DefaultReturnUrl { get; set; } = string.Empty;
    public List<string> AllowedReturnUrls { get; set; } = new();
}

