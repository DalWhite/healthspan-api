using Microsoft.AspNetCore.Identity;

public class User : IdentityUser
{
    public string Name { get; set; }
    public int? Age { get; set; }
    public int? Weight { get; set; }
    public int? Height { get; set; }
    public List<Workout> Workouts { get; set; }
}