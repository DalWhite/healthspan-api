using Microsoft.AspNetCore.Identity;

#nullable enable

public class User : IdentityUser
{
    public string? Name { get; set; }
    public int? Age { get; set; }
    public int? Weight { get; set; }
    public int? Height { get; set; }
    public HealthProfile? HealthProfile { get; set; }
    public List<Workout> Workouts { get; set; } = new();
    public List<ExerciseType> FavoriteExercises { get; set; } = new();
    public User? Trainer { get; set; }
    public List<User> Trainees { get; set; } = new();
    public string? PictureUrl { get; set; }
    public string? GoogleSubject { get; set; }
    public string? GoogleRefreshToken { get; set; }
}