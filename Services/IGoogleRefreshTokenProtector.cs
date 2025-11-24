namespace Services;

#nullable enable

public interface IGoogleRefreshTokenProtector
{
    string? Protect(string? rawToken);
    string? Unprotect(string? protectedToken);
}

