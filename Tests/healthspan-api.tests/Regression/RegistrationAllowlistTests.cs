using System.IO;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using healthspan_api.Options;
using Xunit;

namespace healthspan_api.tests;

/// <summary>
/// t_3798e8dd — production registration is closed to an email allowlist.
/// Covers the fail-safe gate logic, the committed config defaults (the flag
/// can never be "accidentally off"), and the HTTP behavior of POST /register.
/// </summary>
public class RegistrationGateTests
{
    // ---- Gate logic (fail-safe defaults) ----

    [Fact]
    public void DefaultConfig_IsAllowlistMode_AndRejectsEveryone()
    {
        var settings = new RegistrationSettings();

        Assert.True(settings.IsAllowlistMode);
        Assert.False(settings.IsNewAccountAllowed("someone@example.com"));
        Assert.False(settings.IsNewAccountAllowed(null));
    }

    [Fact]
    public void MissingOrGarbageMode_StillAllowlist()
    {
        foreach (var mode in new[] { "", "alowlist", "allow-list", null })
        {
            var settings = new RegistrationSettings
            {
                RegistrationMode = mode,
                AllowedAccounts = { "david.alblanco@gmail.com" },
            };
            Assert.True(settings.IsAllowlistMode, $"mode '{mode}' must not open registration");
            Assert.True(settings.IsNewAccountAllowed(" david.alblanco@gmail.com "));
            Assert.False(settings.IsNewAccountAllowed("intruder@example.com"));
        }
    }

    [Fact]
    public void ExplicitOpenMode_AllowsAnyone()
    {
        var settings = new RegistrationSettings { RegistrationMode = "open" };

        Assert.False(settings.IsAllowlistMode);
        Assert.True(settings.IsNewAccountAllowed("anyone@example.com"));
    }

    [Fact]
    public void Allowlist_MatchesCaseInsensitively()
    {
        var settings = new RegistrationSettings
        {
            RegistrationMode = "Allowlist",
            AllowedAccounts = { "david.alblanco@gmail.com" },
        };

        Assert.True(settings.IsNewAccountAllowed("David.Alblanco@Gmail.COM"));
        Assert.False(settings.IsNewAccountAllowed("david.alblanco@evil.example"));
        Assert.False(settings.IsNewAccountAllowed(""));
    }

    // ---- Committed config defaults (the flag can never be off by accident) ----

    [Fact]
    public void BaseAppsettings_RegisterInAllowlistMode_WithAtLeastOneAccount()
    {
        var root = FindApiProjectRoot();
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "appsettings.json")));
        var auth = doc.RootElement.GetProperty("Auth");

        Assert.Equal("Allowlist", auth.GetProperty("RegistrationMode").GetString());
        Assert.True(auth.GetProperty("AllowedAccounts").GetArrayLength() >= 1);
    }

    [Fact]
    public void DevelopmentAppsettings_RegisterOpen()
    {
        var root = FindApiProjectRoot();
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "appsettings.Development.json")));

        Assert.Equal("Open", doc.RootElement.GetProperty("Auth").GetProperty("RegistrationMode").GetString());
    }

    private static string FindApiProjectRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "healthspan-api.csproj")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate healthspan-api.csproj from the test assembly.");
    }
}

/// <summary>HTTP behavior under the base appsettings (Testing env inherits Allowlist mode).</summary>
public class RegistrationHttpGateTests : IClassFixture<HealthspanApiFactory>
{
    private const string AllowedEmail = "david.alblanco@gmail.com";
    private const string Password = "Str0ng!Passw0rd";

    private readonly HealthspanApiFactory _factory;

    public RegistrationHttpGateTests(HealthspanApiFactory factory) => _factory = factory;

    [Fact]
    public async Task Register_NonAllowlistedEmail_403ProblemDetails()
    {
        var client = _factory.CreateClient();

        var response = await client.PostAsJsonAsync("/register", new { email = "intruder@example.com", password = Password });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        using var payload = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("registration_closed", payload.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Register_AllowlistedEmail_Succeeds_AndExistingLoginStillWorks()
    {
        var client = _factory.CreateClient();

        var register = await client.PostAsJsonAsync("/register", new { email = AllowedEmail, password = Password });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        // Login flow must be untouched by the gate.
        var login = await client.PostAsJsonAsync("/login", new { email = AllowedEmail, password = Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var tokens = JsonDocument.Parse(await login.Content.ReadAsStringAsync());
        Assert.Equal("Bearer", tokens.RootElement.GetProperty("tokenType").GetString());
        Assert.True(tokens.RootElement.GetProperty("accessToken").GetString()?.Length > 0);
    }
}
