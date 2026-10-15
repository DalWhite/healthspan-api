using System.Text.Json;
using Xunit;

namespace healthspan_api.tests.Regression;

public class ExerciseVariantFamiliesTests
{
    private static string FindFamiliesFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "SeedData", "exercise-variant-families.json");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("SeedData/exercise-variant-families.json not found walking up from test assembly directory.");
    }

    [Fact]
    public void Variant_families_file_is_valid_and_self_consistent()
    {
        var json = File.ReadAllText(FindFamiliesFile());
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var doc = JsonSerializer.Deserialize<VariantFamiliesDoc>(json, options);

        Assert.NotNull(doc);
        Assert.NotNull(doc.Families);
        Assert.True(doc.Families.Count >= 30, "expected at least 30 curated families");

        var seenVariants = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var family in doc.Families)
        {
            Assert.False(string.IsNullOrWhiteSpace(family.Parent), "family parent must not be empty");
            Assert.NotEmpty(family.Variants);

            foreach (var variant in family.Variants)
            {
                Assert.False(string.IsNullOrWhiteSpace(variant), "variant name must not be empty");
                Assert.False(string.Equals(variant.Trim(), family.Parent.Trim(), StringComparison.OrdinalIgnoreCase),
                    $"variant '{variant}' must not equal its own parent");
                Assert.True(seenVariants.Add(variant.Trim()),
                    $"variant '{variant}' appears in more than one family");
            }
        }
    }

    private sealed class VariantFamiliesDoc
    {
        public List<VariantFamily> Families { get; set; } = new();
    }

    private sealed class VariantFamily
    {
        public string Parent { get; set; } = string.Empty;
        public List<string> Variants { get; set; } = new();
    }
}
