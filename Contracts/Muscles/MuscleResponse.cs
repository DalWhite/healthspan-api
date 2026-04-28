namespace Contracts.Muscles;

public class MuscleResponse
{
    public int Id { get; set; }
    public string Slug { get; set; }
    public string Name { get; set; }
    public string Side { get; set; }
    public string MuscleGroup { get; set; }
    public int SortOrder { get; set; }
}
