using Mapster;

public class UsersRepository
{
    private readonly MainDbContext _context;
    public UsersRepository(MainDbContext context)
    {
        _context = context;
    }
    public void AddUser(User user)
    {
        _context.Users.Add(user);
        _context.SaveChanges();   
    }
    public User GetUser(int Id)
    {
        return _context.Users.Find(Id);
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