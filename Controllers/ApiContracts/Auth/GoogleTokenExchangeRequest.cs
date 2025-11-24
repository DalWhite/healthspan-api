using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Controllers.ApiContracts.Auth;

public class GoogleTokenExchangeRequest
{
    [Required]
    [JsonPropertyName("idToken")]
    public string IdToken { get; set; } = string.Empty;
}

