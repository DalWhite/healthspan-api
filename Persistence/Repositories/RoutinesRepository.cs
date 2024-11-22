using Microsoft.EntityFrameworkCore;

public class RoutinesRepository(MainDbContext context)
{
    private readonly MainDbContext _context = context;

    public List<Routine> GetRoutines(string userId)
    {
        return _context.Routines
        .Where(r => r.User.Id == userId )
        .Include(r => r.WorkoutTemplates)
        .ThenInclude(wt => wt.ExerciseTypes)
        .ToList();
    }
    public Guid AddRoutine(Routine routine)
    {
        _context.Routines.Add(routine);
        _context.SaveChanges();
        return routine.Id;
    }
}