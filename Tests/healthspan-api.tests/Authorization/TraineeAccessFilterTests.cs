using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Services;
using Xunit;

namespace healthspan_api.tests.Authorization;

/// <summary>
/// Unit tests for the reusable trainer→trainee authorization middleware
/// (TraineeAccessFilter) and the TrainingAccessService lookup helper.
/// </summary>
public class TraineeAccessFilterTests
{
    private const string TrainerId = "trainer-0001";
    private const string OtherTrainerId = "trainer-0002";
    private const string OwnTraineeId = "trainee-0001";
    private const string ForeignTraineeId = "trainee-0002";
    private const string OrphanUserId = "orphan-0001";

    private static CurrentUserProvider CreateAuthenticatedProvider(string? userId)
    {
        var httpContext = new DefaultHttpContext();
        if (userId is not null)
        {
            httpContext.User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(
                    [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId)],
                    "Test"));
        }

        return new CurrentUserProvider(new HttpContextAccessor { HttpContext = httpContext });
    }

    private static MainDbContext CreateContext(CurrentUserProvider userProvider)
    {
        var options = new DbContextOptionsBuilder<MainDbContext>()
            .UseInMemoryDatabase($"TraineeAccessTests_{Guid.NewGuid():N}")
            .Options;

        var context = new MainDbContext(options, userProvider);

        var ownTrainer = new User { Id = TrainerId, Name = "Own Trainer" };
        var otherTrainer = new User { Id = OtherTrainerId, Name = "Other Trainer" };
        context.Users.Add(ownTrainer);
        context.Users.Add(otherTrainer);
        context.Users.Add(new User { Id = OwnTraineeId, Name = "Own Trainee", Trainer = ownTrainer });
        context.Users.Add(new User { Id = ForeignTraineeId, Name = "Foreign Trainee", Trainer = otherTrainer });
        context.Users.Add(new User { Id = OrphanUserId, Name = "Orphan" });
        context.SaveChanges();

        return context;
    }

    /// <summary>
    /// One filter run: filter + ActionExecutingContext + next delegate with a
    /// NextWasCalled flag so tests can assert whether the action executed.
    /// </summary>
    private sealed class Scenario
    {
        public required TraineeAccessFilter Filter { get; init; }
        public required ActionExecutingContext Context { get; init; }
        public ActionExecutionDelegate Next { get; set; } = null!;
        public bool NextWasCalled { get; set; }

        public Task RunAsync() => Filter.OnActionExecutionAsync(Context, Next);
    }

    private static Scenario CreateFilterScenario(
        string? authenticatedUserId,
        Dictionary<string, object?> actionArguments,
        string? filterParameterName = null)
    {
        // NOTE: HttpContextAccessor.HttpContext is backed by a static AsyncLocal holder,
        // so one scenario must use a single CurrentUserProvider for both the filter and
        // the DbContext, otherwise a later accessor would overwrite the ambient context.
        var userProvider = CreateAuthenticatedProvider(authenticatedUserId);
        var httpContext = new DefaultHttpContext();
        if (authenticatedUserId is not null)
        {
            httpContext.User = new System.Security.Claims.ClaimsPrincipal(
                new System.Security.Claims.ClaimsIdentity(
                    [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, authenticatedUserId)],
                    "Test"));
        }

        var context = CreateContext(userProvider);
        var accessService = new TrainingAccessService(context);

        var filter = filterParameterName is null
            ? new TraineeAccessFilter(userProvider, accessService)
            : new TraineeAccessFilter(filterParameterName, userProvider, accessService);

        var actionContext = new ActionContext(httpContext, new Microsoft.AspNetCore.Routing.RouteData(), new Microsoft.AspNetCore.Mvc.Abstractions.ActionDescriptor());
        var executingContext = new ActionExecutingContext(actionContext, [filter], actionArguments, controller: null);

        var scenario = new Scenario
        {
            Filter = filter,
            Context = executingContext,
        };
        scenario.Next = () =>
        {
            scenario.NextWasCalled = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, [filter], controller: null));
        };
        return scenario;
    }

    // ---- Lookup helper (TrainingAccessService.ResolveTraineeUserId) ----

    [Fact]
    public void ResolveTraineeUserId_Resolves_OwnTrainee()
    {
        using var context = CreateContext(CreateAuthenticatedProvider(TrainerId));
        var service = new TrainingAccessService(context);

        service.ResolveTraineeUserId(TrainerId, OwnTraineeId).Should().Be(OwnTraineeId);
    }

    [Fact]
    public void ResolveTraineeUserId_Resolves_Self_WhenTraineeEmpty()
    {
        using var context = CreateContext(CreateAuthenticatedProvider(TrainerId));
        var service = new TrainingAccessService(context);

        service.ResolveTraineeUserId(TrainerId, null).Should().Be(TrainerId);
        service.ResolveTraineeUserId(TrainerId, "").Should().Be(TrainerId);
        service.ResolveTraineeUserId(TrainerId, "   ").Should().Be(TrainerId);
    }

    [Fact]
    public void ResolveTraineeUserId_ReturnsNull_ForForeignTraineeOrUnknownUser()
    {
        using var context = CreateContext(CreateAuthenticatedProvider(TrainerId));
        var service = new TrainingAccessService(context);

        service.ResolveTraineeUserId(TrainerId, ForeignTraineeId).Should().BeNull();
        service.ResolveTraineeUserId(TrainerId, OrphanUserId).Should().BeNull();
        service.ResolveTraineeUserId(TrainerId, "no-such-user").Should().BeNull();
    }

    [Fact]
    public void ResolveTraineeUserId_ReturnsNull_ForEmptyTrainer()
    {
        using var context = CreateContext(CreateAuthenticatedProvider(TrainerId));
        var service = new TrainingAccessService(context);

        service.ResolveTraineeUserId("", OwnTraineeId).Should().BeNull();
        service.ResolveTraineeUserId("   ", OwnTraineeId).Should().BeNull();
    }

    // ---- Filter behavior ----

    [Fact]
    public async Task Filter_AllowsOwnTrainee_AndKeepsActionRunning()
    {
        var scenario = CreateFilterScenario(
            TrainerId,
            new Dictionary<string, object?> { ["traineeId"] = OwnTraineeId });

        await scenario.RunAsync();

        scenario.Context.Result.Should().BeNull();
        scenario.NextWasCalled.Should().BeTrue();
        scenario.Context.ActionArguments["traineeId"].Should().Be(OwnTraineeId);
    }

    [Fact]
    public async Task Filter_DeniesForeignTrainee_With403AndNoActionExecution()
    {
        var scenario = CreateFilterScenario(
            TrainerId,
            new Dictionary<string, object?> { ["traineeId"] = ForeignTraineeId });

        await scenario.RunAsync();

        scenario.Context.Result.Should().BeOfType<ForbidResult>();
        scenario.NextWasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Filter_DeniesOrphanUser_AndUnknownUser()
    {
        var orphan = CreateFilterScenario(
            TrainerId,
            new Dictionary<string, object?> { ["traineeId"] = OrphanUserId });
        await orphan.RunAsync();
        orphan.Context.Result.Should().BeOfType<ForbidResult>();
        orphan.NextWasCalled.Should().BeFalse();

        var unknown = CreateFilterScenario(
            TrainerId,
            new Dictionary<string, object?> { ["traineeId"] = "no-such-user" });
        await unknown.RunAsync();
        unknown.Context.Result.Should().BeOfType<ForbidResult>();
        unknown.NextWasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Filter_ResolvesEmptyTraineeToAuthenticatedTrainer()
    {
        var scenario = CreateFilterScenario(
            TrainerId,
            new Dictionary<string, object?> { ["traineeId"] = "" });

        await scenario.RunAsync();

        scenario.Context.Result.Should().BeNull();
        scenario.NextWasCalled.Should().BeTrue();
        scenario.Context.ActionArguments["traineeId"].Should().Be(TrainerId);
    }

    [Fact]
    public async Task Filter_AllowsWhenNoUserFilterParameterPresent()
    {
        var scenario = CreateFilterScenario(
            TrainerId,
            new Dictionary<string, object?> { ["workoutId"] = "w1" });

        await scenario.RunAsync();

        scenario.Context.Result.Should().BeNull();
        scenario.NextWasCalled.Should().BeTrue();
    }

    [Fact]
    public async Task Filter_ProbesTraineeIdBeforeUserId()
    {
        var scenario = CreateFilterScenario(
            TrainerId,
            new Dictionary<string, object?> { ["traineeId"] = ForeignTraineeId, ["userId"] = OwnTraineeId });

        await scenario.RunAsync();

        scenario.Context.Result.Should().BeOfType<ForbidResult>();
        scenario.NextWasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Filter_UsesExplicitParameterName()
    {
        var scenario = CreateFilterScenario(
            TrainerId,
            new Dictionary<string, object?> { ["userId"] = OwnTraineeId },
            filterParameterName: "userId");

        await scenario.RunAsync();

        scenario.Context.Result.Should().BeNull();
        scenario.NextWasCalled.Should().BeTrue();
        scenario.Context.ActionArguments["userId"].Should().Be(OwnTraineeId);
    }

    [Fact]
    public async Task Filter_ReturnsUnauthorized_WhenNoAuthenticatedTrainer()
    {
        var scenario = CreateFilterScenario(
            null,
            new Dictionary<string, object?> { ["traineeId"] = OwnTraineeId });

        await scenario.RunAsync();

        scenario.Context.Result.Should().BeOfType<UnauthorizedResult>();
        scenario.NextWasCalled.Should().BeFalse();
    }

    [Fact]
    public async Task Filter_RewritesDeclaredParameter_OmittedFromActionArguments()
    {
        // MVC omits optional query parameters that are not supplied from
        // ActionArguments; the filter must still resolve and add the key so the
        // action receives the trainer's own id instead of the C# default.
        var userProvider = CreateAuthenticatedProvider(TrainerId);
        using var context = CreateContext(userProvider);
        var accessService = new TrainingAccessService(context);
        var filter = new TraineeAccessFilter("userId", userProvider, accessService);

        var httpContext = new DefaultHttpContext();
        httpContext.User = new System.Security.Claims.ClaimsPrincipal(
            new System.Security.Claims.ClaimsIdentity(
                [new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, TrainerId)],
                "Test"));

        var actionDescriptor = new ControllerActionDescriptor
        {
            ControllerName = "ExerciseTypes",
            ActionName = "GetTrainedExerciseTypes",
            Parameters = [new ParameterDescriptor { Name = "userId", ParameterType = typeof(string) }]
        };

        var actionContext = new ActionContext(httpContext, new Microsoft.AspNetCore.Routing.RouteData(), actionDescriptor);
        var executingContext = new ActionExecutingContext(
            actionContext, [filter], new Dictionary<string, object?>(), controller: null);

        var nextWasCalled = false;
        ActionExecutionDelegate next = () =>
        {
            nextWasCalled = true;
            return Task.FromResult(new ActionExecutedContext(actionContext, [filter], controller: null));
        };

        await filter.OnActionExecutionAsync(executingContext, next);

        executingContext.Result.Should().BeNull();
        nextWasCalled.Should().BeTrue();
        executingContext.ActionArguments["userId"].Should().Be(TrainerId);
    }

    [Fact]
    public void Attribute_WiresToFilterType()
    {
        var userProvider = CreateAuthenticatedProvider(TrainerId);
        using var context = CreateContext(userProvider);
        var accessService = new TrainingAccessService(context);

        var services = new ServiceCollection();
        services.AddSingleton(userProvider);
        services.AddSingleton(accessService);
        var provider = services.BuildServiceProvider();

        ((IFilterFactory)new TraineeAccessAttribute()).CreateInstance(provider)
            .Should().BeOfType<TraineeAccessFilter>();
        ((IFilterFactory)new TraineeAccessAttribute("userId")).CreateInstance(provider)
            .Should().BeOfType<TraineeAccessFilter>();
    }
}
