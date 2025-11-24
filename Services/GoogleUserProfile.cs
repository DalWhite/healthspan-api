namespace Services;

#nullable enable

public record GoogleUserProfile(
    string Subject,
    string? Email,
    bool EmailVerified,
    string? Name,
    string? PictureUrl);

