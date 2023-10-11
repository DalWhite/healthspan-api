using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class Workout
{
    public int Id { get; set; }
    public DateTime? Date { get; set; }
    public int UserId { get; set; }
    public User User {get;set;}
    public List<Exercise> Exercises { get; set; }
}