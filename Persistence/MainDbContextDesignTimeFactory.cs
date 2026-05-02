using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using Services;

public class MainDbContextDesignTimeFactory : IDesignTimeDbContextFactory<MainDbContext>
{
    public MainDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<MainDbContext>();
        optionsBuilder.UseMySql(
            "Server=localhost;Port=3306;Database=healthspan;Uid=design;Pwd=design;",
            new MySqlServerVersion(new Version(8, 0, 36)));

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        };

        var currentUserProvider = new CurrentUserProvider(httpContextAccessor);
        return new MainDbContext(optionsBuilder.Options, currentUserProvider);
    }
}
