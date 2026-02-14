using Microsoft.EntityFrameworkCore;

public class MuscleRepository
{
    private readonly MainDbContext _context;

    public MuscleRepository(MainDbContext context)
    {
        _context = context;
    }

    public async Task<Muscle> GetMuscleByIdAsync(int id)
    {
        return await _context.Muscles
            .Where(m => m.Id == id)
            .FirstOrDefaultAsync();
    }

    public async Task<List<Muscle>> GetAllMusclesAsync()
    {
        return await _context.Muscles
            .ToListAsync();
    }

    public async Task<Muscle> GetByNameAsync(string name)
    {
        return await _context.Muscles
            .Where(m => m.Name.ToLower() == name.ToLower())
            .FirstOrDefaultAsync();
    }
}