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
            .Where(m => m.SortOrder > 0 && (m.Slug == null || !m.Slug.StartsWith("legacy-")))
            .OrderBy(m => m.SortOrder)
            .ThenBy(m => m.Name)
            .ToListAsync();
    }

    public async Task<Muscle> GetByNameAsync(string name)
    {
        var resolvedSlug = MuscleCatalogSeeder.TryResolveSlug(name);
        if (!string.IsNullOrWhiteSpace(resolvedSlug))
        {
            return await GetBySlugAsync(resolvedSlug);
        }

        return await _context.Muscles.FirstOrDefaultAsync(m => m.Name.ToLower() == name.ToLower());
    }

    public async Task<Muscle> GetBySlugAsync(string slug)
    {
        return await _context.Muscles.FirstOrDefaultAsync(m => m.Slug != null && m.Slug.ToLower() == slug.ToLower());
    }

    public async Task<bool> AllExistAsync(IEnumerable<int> ids)
    {
        var distinctIds = ids.Distinct().ToList();
        if (distinctIds.Count == 0)
        {
            return true;
        }

        var existingCount = await _context.Muscles.CountAsync(m =>
            distinctIds.Contains(m.Id) &&
            m.SortOrder > 0 &&
            (m.Slug == null || !m.Slug.StartsWith("legacy-")));
        return existingCount == distinctIds.Count;
    }
}
