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
const string CorsPolicyName = "MyAllowSpecificOrigins";

var dbConnectionString = builder.Configuration.GetSection("DbConnectionString").Value;
builder.Services.Configure<GoogleAuthSettings>(builder.Configuration);
builder.Services.Configure<AzureBlobStorageSettings>(builder.Configuration);
builder.Services.Configure<FileSystemDocumentStorageSettings>(builder.Configuration);
builder.Services.Configure<AzureDocumentIntelligenceSettings>(builder.Configuration);
builder.Services.Configure<LlmMarkerNormalizationSettings>(builder.Configuration);
builder.Services.Configure<StripeSettings>(builder.Configuration.GetSection(StripeSettings.SectionName));
builder.Services.Configure<healthspan_api.Options.SponsoredOffersSettings>(builder.Configuration);
builder.Services.Configure<healthspan_api.Options.DevQaSeedSettings>(builder.Configuration.GetSection(healthspan_api.Options.DevQaSeedSettings.SectionName));
builder.Services.Configure<healthspan_api.Options.RegistrationSettings>(builder.Configuration.GetSection(healthspan_api.Options.RegistrationSettings.SectionName));
var googleSettings = builder.Configuration.Get<GoogleAuthSettings>() ?? new GoogleAuthSettings();
builder.Services.AddMemoryCache();

var authenticationBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = IdentityConstants.BearerScheme;
    options.DefaultAuthenticateScheme = IdentityConstants.BearerScheme;
    options.DefaultChallengeScheme = IdentityConstants.BearerScheme;
});

authenticationBuilder.AddBearerToken(IdentityConstants.BearerScheme);
if (!string.IsNullOrWhiteSpace(googleSettings.ClientId))
{
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
}
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
        options.AddPolicy(name: CorsPolicyName,
                          policyBuilder =>
                          {
                              policyBuilder.WithOrigins("https://localhost:5001", "https://salmon-island-045222d03.4.azurestaticapps.net", "http://localhost:5000")
                                           .AllowAnyMethod()
                                           .AllowAnyHeader()
                                           .WithExposedHeaders("X-Total-Count");
                          });
    });


builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient();
builder.Services.AddScoped<CurrentUserProvider>();
builder.Services.AddScoped<UsersRepository>();
builder.Services.AddScoped<MuscleRepository>();
builder.Services.AddScoped<WorkoutsRepository>();
builder.Services.AddScoped<ExercisesRepository>();
builder.Services.AddScoped<TrainingTrackerRepository>();
builder.Services.AddScoped<RoutinesRepository>();
builder.Services.AddScoped<ExerciseTypesRepository>();
builder.Services.AddScoped<BloodTestsRepository>();
builder.Services.AddScoped<GalpinAssessmentsRepository>();
builder.Services.AddScoped<WellnessActivitiesRepository>();
builder.Services.AddScoped<WellnessActivityService>();
builder.Services.AddScoped<ExerciseMetricsService>();
builder.Services.AddScoped<ResultsService>();
builder.Services.AddScoped<WeeklyTrainingSummaryService>();
builder.Services.AddScoped<TrainingTrackerAnalyticsService>();
builder.Services.AddScoped<RoutinePlanService>();
builder.Services.AddScoped<RoutineAdherenceService>();
builder.Services.AddScoped<HealthProfileService>();
builder.Services.AddScoped<TrainingAccessService>();
builder.Services.AddScoped<SyncService>();
builder.Services.AddHttpClient<IBillingGateway, StripeBillingGateway>();
builder.Services.AddScoped<BillingService>();
builder.Services.AddScoped<BloodAnalyticsService>();
builder.Services.AddSingleton<BloodTestProcessingChannel>();
builder.Services.AddHostedService<BloodTestProcessingWorker>();
builder.Services.AddScoped<GalpinAssessmentCatalogService>();
builder.Services.AddScoped<GalpinAssessmentService>();
builder.Services.AddScoped<HealthAccessService>();
builder.Services.AddScoped<SponsoredOffersRepository>();
builder.Services.AddScoped<SponsoredOfferService>();
builder.Services.AddScoped<HealthDashboardService>();
builder.Services.AddScoped<UserDataExportService>();
builder.Services.AddScoped<AzureBlobBloodTestDocumentStorage>();
builder.Services.AddScoped<FileSystemBloodTestDocumentStorage>();
builder.Services.AddScoped<IBloodTestDocumentStorage, HybridBloodTestDocumentStorage>();
builder.Services.AddScoped<CsvService>();
builder.Services.AddSingleton<CsvImportJobService>();
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

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider.GetRequiredService<MainDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

    try
    {
        if (db.Database.IsRelational())
        {
            await db.Database.MigrateAsync();
        }

        await MuscleCatalogSeeder.UpsertAsync(db);
        await MuscleCatalogSeeder.BackfillExerciseTypeAssignmentsAsync(db);

        // Universal training tracker: the 9 adaptations in canonical order, then
        // a heuristic mapping only for exercise types that have none yet.
        await AdaptationCatalogSeeder.UpsertAsync(db);
        await AdaptationCatalogSeeder.BackfillExerciseTypeAdaptationsAsync(db);

        // Healthspan Premium plans (billing MVP, t_c0e8a770).
        await BillingPlanSeeder.UpsertAsync(db);

        // Monthly health KPIs (t_358e09e9/t_4882e760): the weight_kg metric
        // definition and the seeded (editable) "basic" strength tag set.
        // Relational only: the integration-test host (InMemory) is seeded by
        // TestDataSeeder instead, and must own the exercise-type baseline.
        if (db.Database.IsRelational())
        {
            await BodyCompositionCatalogSeeder.UpsertAsync(db);
            await StrengthBasicsCatalogSeeder.UpsertAsync(db);
        }

        // QA seed account (t_ebb6967c). DISABLED by default; only the dev
        // deploy pipeline sets DevQaSeed:Enabled=true. Production never seeds it.
        var devQaSeedSettings = scope.ServiceProvider
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<healthspan_api.Options.DevQaSeedSettings>>()
            .Value;
        if (devQaSeedSettings.Enabled)
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            await DevQaAccountSeeder.UpsertAsync(db, userManager, devQaSeedSettings);
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to apply muscle catalog startup migration and seed logic.");
        throw;
    }
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseCors(CorsPolicyName);
app.UseAuthentication();
app.UseAuthorization();

// Registration gate (t_3798e8dd): MapIdentityApi maps POST /register, which has
// no hook of its own, so the email allowlist is enforced here. Only account
// CREATION is gated; /login and /refresh are untouched. The Google paths are
// gated inside GoogleAuthService (the email is only known after id-token
// validation).
app.Use(async (context, next) =>
{
    if (HttpMethods.IsPost(context.Request.Method) &&
        context.Request.Path.Equals("/register", StringComparison.OrdinalIgnoreCase))
    {
        var registration = context.RequestServices
            .GetRequiredService<Microsoft.Extensions.Options.IOptions<healthspan_api.Options.RegistrationSettings>>()
            .Value;
        if (registration.IsAllowlistMode)
        {
            string? email = null;
            context.Request.EnableBuffering();
            try
            {
                using var payload = await JsonDocument.ParseAsync(context.Request.Body);
                if (payload.RootElement.TryGetProperty("email", out var emailElement) &&
                    emailElement.ValueKind == JsonValueKind.String)
                {
                    email = emailElement.GetString();
                }
            }
            catch (JsonException)
            {
                // Malformed body: let the Identity endpoint answer 400 as usual.
            }
            finally
            {
                context.Request.Body.Position = 0;
            }

            if (!registration.IsNewAccountAllowed(email))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(
                    ApiProblemResult.RegistrationClosedProblem(),
                    (System.Text.Json.JsonSerializerOptions?)null,
                    "application/problem+json");
                return;
            }
        }
    }

    await next();
});

// Add Swagger middleware
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Healthspan API V1");
    c.RoutePrefix = "swagger";
});

app.MapIdentityApi<User>()
   .RequireCors(CorsPolicyName);
app.MapControllers()
   .RequireCors(CorsPolicyName);

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
.RequireCors(CorsPolicyName)
.RequireAuthorization();

app.Run();

// Make the implicit Program class public so test projects can access it
public partial class Program { }
