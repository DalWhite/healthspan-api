using Mapster;
using Services;

public class UsersRepository
{
    private readonly MainDbContext _context;
    private readonly UserProvider _userProvider;
    public UsersRepository(MainDbContext context, UserProvider userProvider)
    {
        _context = context;
        _userProvider = userProvider;
    }
    public void AddUser(User user)
    {
        _context.Users.Add(user);
        _context.SaveChanges();   
    }
    public User GetUser(string Id)
    {
        return _context.Users.Find(Id);
    }
    public List<User> GetTrainees()
    {
        var currentUserId = _userProvider.GetUserId();
        return _context.Users.Where(u => u.Trainer.Id == currentUserId)
            .ToList();
    }
    public void UpdateUser(User user)
    {
        var existingEntity = GetUser(user.Id);

        if (user.Name is not null)
        {
            existingEntity.Name = user.Name;
        }
        if (user.Age is not null)
        {
            existingEntity.Age = user.Age;
        }
        if (user.Weight is not null)
        {
            existingEntity.Weight = user.Weight;
        }
        if (user.Height is not null)
        {
            existingEntity.Height = user.Height;
        }
        _context.SaveChanges();
    }
}