using System.Text.Json.Serialization;

namespace Controllers.ApiContracts.ExerciseTypes;

public class ExternalExerciseDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("force")]
    public string Force { get; set; }

    [JsonPropertyName("level")]
    public string Level { get; set; }

    [JsonPropertyName("mechanic")]
    public string Mechanic { get; set; }

    [JsonPropertyName("equipment")]
    public string Equipment { get; set; }

    [JsonPropertyName("primaryMuscles")]
    public List<string> PrimaryMuscles { get; set; }

    [JsonPropertyName("secondaryMuscles")]
    public List<string> SecondaryMuscles { get; set; }

    [JsonPropertyName("instructions")]
    public List<string> Instructions { get; set; }

    [JsonPropertyName("category")]
    public string Category { get; set; }

    [JsonPropertyName("images")]
    public List<string> Images { get; set; }
}
