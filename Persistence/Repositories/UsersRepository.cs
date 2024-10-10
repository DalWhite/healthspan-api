using Mapster;
using Microsoft.AspNetCore.Identity;
using Services;

public class UsersRepository
{
    private readonly MainDbContext _context;
    private readonly CurrentUserProvider _userProvider;
    public UsersRepository(MainDbContext context, CurrentUserProvider userProvider)
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
    public List<Role> GetUserRoles(string userId)
    {
        var userRoleIds = _context.UserRoles.Where(ur => ur.UserId == userId).Select(r=>r.RoleId).ToList();
        return _context.Roles.Where(r => userRoleIds.Contains(r.Id)).ToList();
    }
    public List<User> GetTrainees()
    {
        var currentUserId = _userProvider.GetCurrentUserId();
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