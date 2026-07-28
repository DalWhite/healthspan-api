using System.ComponentModel.DataAnnotations;

public static class ExerciseTagNormalizer
{
    public const int MaximumCount = 5;
    public const int MaximumLength = 30;

    public static List<string> Normalize(IEnumerable<string> tags)
        => (tags ?? [])
            .Select(NormalizeTag)
            .Where(tag => tag.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    public static bool Matches(IEnumerable<string> tags, string tag)
    {
        var normalizedFilter = NormalizeTag(tag);
        return normalizedFilter.Length == 0
            || Normalize(tags).Contains(
                normalizedFilter,
                StringComparer.OrdinalIgnoreCase);
    }

    public static IEnumerable<ValidationResult> Validate(
        IEnumerable<string> tags)
    {
        var normalized = Normalize(tags);
        if (normalized.Count > MaximumCount)
        {
            yield return new ValidationResult(
                $"Use no more than {MaximumCount} training tags.",
                ["Tags"]);
        }

        if (normalized.Any(tag => tag.Length > MaximumLength))
        {
            yield return new ValidationResult(
                $"Training tags must be {MaximumLength} characters or fewer.",
                ["Tags"]);
        }

        if (normalized.Any(tag => tag.Any(char.IsControl)))
        {
            yield return new ValidationResult(
                "Training tags cannot contain control characters.",
                ["Tags"]);
        }
    }

    private static string NormalizeTag(string tag)
        => string.Join(
            " ",
            (tag ?? string.Empty)
                .Trim()
                .TrimStart('#')
                .Split(
                    ' ',
                    StringSplitOptions.RemoveEmptyEntries
                    | StringSplitOptions.TrimEntries));
}
