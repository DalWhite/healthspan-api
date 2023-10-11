using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class User
{
    public int Id { get; set; }
    public string Name { get; set; }
    public int? Age { get; set; }
    public int? Weight { get; set; }
    public int? Height { get; set; }
    public List<Workout> Workouts { get; set; }
}