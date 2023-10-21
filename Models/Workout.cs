using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class Workout
{
    public Guid Id { get; set; }
    public DateTime? Date { get; set; }
    public string Type { get; set; }
    public User User {get;set;}
    public List<Exercise> Exercises { get; set; }
}