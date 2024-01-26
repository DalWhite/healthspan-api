using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var dbConnectionString = builder.Configuration.GetSection("DbConnectionString").Value;
builder.Services.AddAuthentication()
    .AddBearerToken(IdentityConstants.BearerScheme);
    // .AddGoogle(options =>
    //  {
    //      options.ClientId = builder.Configuration.GetSection("GoogleClientId").Value;
    //      options.ClientSecret = builder.Configuration.GetSection("GoogleClientSecret").Value;
    //  });
builder.Services.AddAuthorizationBuilder();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddControllers();


builder.Services.AddDbContext<MainDbContext>(
       options => options.UseMySQL(dbConnectionString));

builder.Services.AddIdentityCore<User>()
    .AddEntityFrameworkStores<MainDbContext>()
    .AddApiEndpoints();

builder.Services.AddCors(options =>
{
    options.AddPolicy(name: "any",
                      policy =>
                      {
                          policy.AllowAnyOrigin();
                          policy.AllowAnyHeader();
                          policy.SetIsOriginAllowedToAllowWildcardSubdomains();
                      });
});

builder.Services.AddScoped<UsersRepository>();
builder.Services.AddScoped<WorkoutsRepository>();
builder.Services.AddScoped<ExercisesRepository>();
builder.Services.AddScoped<ExerciseTypesRepository>();


var app = builder.Build();

app.UseCors("any");
app.MapControllers().RequireCors("any");
app.MapIdentityApi<User>().RequireCors("any");

app.UseSwagger();
app.UseSwaggerUI();


app.UseHttpsRedirection();


app.Run();
