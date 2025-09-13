using healthspan_api.Persistence.Repositories;
using MediatR;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Scalar.AspNetCore;
using Services;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;


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

builder.Services.AddControllers();


builder.Services.AddDbContext<MainDbContext>(
       options => options.UseMySQL(dbConnectionString));

builder.Services.AddIdentityCore<User>()
    .AddRoles<Role>()
    .AddEntityFrameworkStores<MainDbContext>()
    .AddApiEndpoints();
builder.Services.AddCors(options =>
    {
        options.AddPolicy(name: "MyAllowSpecificOrigins",
                          builder =>
                          {
                              builder.WithOrigins("https://localhost:5001", "https://salmon-island-045222d03.4.azurestaticapps.net")
                                     .AllowAnyMethod()
                                     .AllowAnyHeader();
                          });
    });


builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<CurrentUserProvider>();
builder.Services.AddScoped<UsersRepository>();
builder.Services.AddScoped<MuscleRepository>();
builder.Services.AddScoped<WorkoutsRepository>();
builder.Services.AddScoped<ExercisesRepository>();
builder.Services.AddScoped<RoutinesRepository>();
builder.Services.AddScoped<ExerciseTypesRepository>();
builder.Services.AddScoped<ExerciseMetricsService>();
builder.Services.AddScoped<CsvService>();
builder.Services.AddMediatR(typeof(Program).Assembly);

// Comment out OpenAPI/Scalar
// builder.Services.AddOpenApi();

// Add Swagger
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "Healthspan API", Version = "v1" });

    // Add JWT Bearer Auth to Swagger
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT"
    });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
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
            new string[] {}
        }
    });
});

var app = builder.Build();

app.UseHttpsRedirection();
app.UseCors("MyAllowSpecificOrigins");

// Add Swagger middleware
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Healthspan API V1");
    c.RoutePrefix = "swagger";
});

app.MapIdentityApi<User>();
app.MapControllers();

// Comment out OpenAPI/Scalar
// if (app.Environment.IsDevelopment())
// {
//     app.MapOpenApi();
//     app.MapScalarApiReference(options => {
//         options
//             .WithTitle("Healthspan api")
//             .WithTheme(ScalarTheme.DeepSpace)
//             .WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient)
//             .WithHttpBearerAuthentication(bearerOptions => bearerOptions.Token = "HToken");
//     });
// }

app.MapPost("/Logout", async (SignInManager<User> signInManager,
    [FromBody] object empty) =>
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
