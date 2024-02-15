using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Services;

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

AddSwagger(builder);
builder.Services.AddControllers();


builder.Services.AddDbContext<MainDbContext>(
       options => options.UseMySQL(dbConnectionString));

builder.Services.AddIdentityCore<User>()
    .AddEntityFrameworkStores<MainDbContext>()
    .AddApiEndpoints();
builder.Services.AddCors(options =>
    {
        options.AddPolicy(name: "MyAllowSpecificOrigins",
                          builder =>
                          {
                              builder.WithOrigins("https://localhost:5001")
                                     .AllowAnyMethod()
                                     .AllowAnyHeader();
                          });
    });

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<UserProvider>();
builder.Services.AddScoped<UsersRepository>();
builder.Services.AddScoped<WorkoutsRepository>();
builder.Services.AddScoped<ExercisesRepository>();
builder.Services.AddScoped<ExerciseTypesRepository>();

var app = builder.Build();

app.UseHttpsRedirection();
app.UseCors("MyAllowSpecificOrigins");

app.MapIdentityApi<User>();
app.MapControllers();

app.UseSwagger();
app.UseSwaggerUI();

app.MapPost("/Logout", async (SignInManager<User> signInManager,
    [FromBody]object empty) =>
{
    if (empty != null)
    {
        await signInManager.SignOutAsync();
        return Results.Ok();
    }
    return Results.Unauthorized();
})
.WithOpenApi()
.RequireAuthorization();

app.Run();

static void AddSwagger(WebApplicationBuilder builder)
{
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo { Title = "Healthspan api", Version = "v1" });

        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
        {
            In = ParameterLocation.Header,
            Description = "Please enter a valid token",
            Name = "Authorization",
            Type = SecuritySchemeType.Http,
            BearerFormat = "JWT",
            Scheme = "Bearer"
        });

        options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
    });
}