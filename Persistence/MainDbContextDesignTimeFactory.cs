using System;
using System.IO;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using Services;

public class MainDbContextDesignTimeFactory : IDesignTimeDbContextFactory<MainDbContext>
{
    public MainDbContext CreateDbContext(string[] args)
    {
        var configuration = BuildConfiguration();
        var dbConnectionString = configuration.GetValue<string>("DbConnectionString");

        if (string.IsNullOrWhiteSpace(dbConnectionString) ||
            string.Equals(dbConnectionString, "secret", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "DbConnectionString is required for EF Core design-time operations. " +
                "Provide it through appsettings, user secrets, or environment variables.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<MainDbContext>();
        optionsBuilder.UseMySql(
            dbConnectionString,
            new MySqlServerVersion(new Version(8, 0, 36)));

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext()
        };

        var currentUserProvider = new CurrentUserProvider(httpContextAccessor);
        return new MainDbContext(optionsBuilder.Options, currentUserProvider);
    }

    private static IConfiguration BuildConfiguration()
    {
        var environmentName = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT")
            ?? Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT");

        var configurationBuilder = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true);

        if (!string.IsNullOrWhiteSpace(environmentName))
        {
            configurationBuilder.AddJsonFile($"appsettings.{environmentName}.json", optional: true);
        }

        return configurationBuilder
            .AddUserSecrets<Program>(optional: true)
            .AddEnvironmentVariables()
            .Build();
    }
}
