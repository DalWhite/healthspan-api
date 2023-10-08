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
    public User GetUser(Guid Id)
    {
        return _context.Users.Find(Id);
    }
    public void UpdateUser(User user)
    {
        _context.Users.Update(user);
        _context.SaveChanges();
    }
}