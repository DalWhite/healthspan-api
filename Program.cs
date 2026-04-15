using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using healthspan_api.Options;
using healthspan_api.Persistence.Repositories;
using MediatR;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.OAuth.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Scalar.AspNetCore;
using Services;
using Pomelo.EntityFrameworkCore.MySql.Infrastructure;
using System.Text.Json;


var builder = WebApplication.CreateBuilder(args);

var dbConnectionString = builder.Configuration.GetSection("DbConnectionString").Value;
builder.Services.Configure<GoogleAuthSettings>(builder.Configuration);
builder.Services.Configure<AzureBlobStorageSettings>(builder.Configuration);
builder.Services.Configure<FileSystemDocumentStorageSettings>(builder.Configuration);
builder.Services.Configure<AzureDocumentIntelligenceSettings>(builder.Configuration);
builder.Services.Configure<LlmMarkerNormalizationSettings>(builder.Configuration);
var googleSettings = builder.Configuration.Get<GoogleAuthSettings>() ?? new GoogleAuthSettings();
builder.Services.AddMemoryCache();

var authenticationBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = IdentityConstants.BearerScheme;
    options.DefaultAuthenticateScheme = IdentityConstants.BearerScheme;
    options.DefaultChallengeScheme = IdentityConstants.BearerScheme;
});

authenticationBuilder.AddBearerToken(IdentityConstants.BearerScheme);
authenticationBuilder.AddGoogle(options =>
{
    options.ClientId = googleSettings.ClientId;
    options.ClientSecret = googleSettings.ClientSecret;

    var callbackUri = googleSettings.CallbackUri;
    if (!string.IsNullOrWhiteSpace(callbackUri) && Uri.TryCreate(callbackUri, UriKind.Absolute, out var parsedCallback))
    {
        options.CallbackPath = parsedCallback.AbsolutePath;
    }
    else
    {
        options.CallbackPath = "/auth/google/callback";
    }

    options.SignInScheme = IdentityConstants.ExternalScheme;
    options.UsePkce = true;
    options.SaveTokens = true;
    options.AccessType = "offline";
    options.Scope.Add("openid");
    options.Scope.Add("profile");
    options.Scope.Add("email");
    options.Events.OnRedirectToAuthorizationEndpoint = context =>
    {
        var separator = context.RedirectUri.Contains('?') ? "&" : "?";
        context.Response.Redirect($"{context.RedirectUri}{separator}prompt=consent");
        return Task.CompletedTask;
    };
    options.Events.OnCreatingTicket = context =>
    {
        if (context.User.TryGetProperty("picture", out var pictureElement))
        {
            var picture = pictureElement.GetString();
            if (!string.IsNullOrWhiteSpace(picture))
            {
                context.Identity?.AddClaim(new Claim("picture", picture));
            }
        }

        if (context.User.TryGetProperty("email_verified", out var emailVerifiedElement) &&
            (emailVerifiedElement.ValueKind == JsonValueKind.True ||
             emailVerifiedElement.ValueKind == JsonValueKind.False))
        {
            context.Identity?.AddClaim(new Claim("email_verified", emailVerifiedElement.GetBoolean().ToString()));
        }

        return Task.CompletedTask;
    };
});
builder.Services.AddAuthorizationBuilder();

builder.Services.AddEndpointsApiExplorer();

builder.Services.AddControllers();

builder.Services.AddDbContext<MainDbContext>(options => 
    options.UseMySql(
        dbConnectionString,
        ServerVersion.AutoDetect(dbConnectionString)
    )
);

builder.Services.AddIdentityCore<User>()
    .AddRoles<Role>()
    .AddEntityFrameworkStores<MainDbContext>()
    .AddSignInManager()
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
builder.Services.AddHttpClient();
builder.Services.AddScoped<CurrentUserProvider>();
builder.Services.AddScoped<UsersRepository>();
builder.Services.AddScoped<MuscleRepository>();
builder.Services.AddScoped<WorkoutsRepository>();
builder.Services.AddScoped<ExercisesRepository>();
builder.Services.AddScoped<RoutinesRepository>();
builder.Services.AddScoped<ExerciseTypesRepository>();
builder.Services.AddScoped<BloodTestsRepository>();
builder.Services.AddScoped<GalpinAssessmentsRepository>();
builder.Services.AddScoped<ExerciseMetricsService>();
builder.Services.AddScoped<BloodAnalyticsService>();
builder.Services.AddSingleton<BloodTestProcessingChannel>();
builder.Services.AddHostedService<BloodTestProcessingWorker>();
builder.Services.AddScoped<GalpinAssessmentCatalogService>();
builder.Services.AddScoped<GalpinAssessmentService>();
builder.Services.AddScoped<HealthAccessService>();
builder.Services.AddScoped<HealthDashboardService>();
builder.Services.AddScoped<AzureBlobBloodTestDocumentStorage>();
builder.Services.AddScoped<FileSystemBloodTestDocumentStorage>();
builder.Services.AddScoped<IBloodTestDocumentStorage, HybridBloodTestDocumentStorage>();
builder.Services.AddScoped<CsvService>();
builder.Services.AddScoped<GoogleAuthService>();
builder.Services.AddScoped<GoogleTokenValidator>();
builder.Services.AddScoped<GoogleReturnUrlResolver>();
builder.Services.AddScoped<LocalTokenService>();
builder.Services.AddScoped<IGoogleRefreshTokenProtector, GoogleRefreshTokenProtector>();
builder.Services.AddScoped<IOcrExtractionProvider, AzureDocumentIntelligenceOcrProvider>();
builder.Services.AddScoped<NoOpMarkerNormalizationProvider>();
builder.Services.AddScoped<ConfiguredMarkerNormalizationProvider>();
builder.Services.AddScoped<IMarkerNormalizationProvider, ConfiguredMarkerNormalizationProvider>();
builder.Services.AddHttpClient<GoogleOAuthClient>();
builder.Services.AddHttpClient<OpenRouterMarkerNormalizationProvider>((serviceProvider, client) =>
{
    var settings = serviceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<LlmMarkerNormalizationSettings>>().Value;
    client.BaseAddress = new Uri(settings.BaseUrl);
    client.Timeout = TimeSpan.FromSeconds(Math.Max(30, settings.TimeoutSeconds));
});
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

app.MapPost("/Logout", async (
    SignInManager<User> signInManager,
    UserManager<User> userManager,
    IHttpContextAccessor httpContextAccessor,
    GoogleOAuthClient googleOAuthClient,
    IGoogleRefreshTokenProtector refreshTokenProtector,
    ILogger<Program> logger,
    CancellationToken cancellationToken) =>
{
    var userId = httpContextAccessor.HttpContext?.User?.FindFirstValue(ClaimTypes.NameIdentifier);
    if (!string.IsNullOrEmpty(userId))
    {
        var user = await userManager.FindByIdAsync(userId);
        if (user?.GoogleRefreshToken != null)
        {
            var refreshToken = refreshTokenProtector.Unprotect(user.GoogleRefreshToken);

            if (!string.IsNullOrWhiteSpace(refreshToken))
            {
                await googleOAuthClient.RevokeRefreshTokenAsync(refreshToken, cancellationToken);
            }

            user.GoogleRefreshToken = null;
            var updateResult = await userManager.UpdateAsync(user);
            if (!updateResult.Succeeded)
            {
                logger.LogWarning("Failed to persist Google refresh token removal for user {UserId}", userId);
            }
        }
    }

    await signInManager.SignOutAsync();
    return Results.Ok();
})
.WithOpenApi()
.RequireAuthorization();

app.Run();

// Make the implicit Program class public so test projects can access it
public partial class Program { }
