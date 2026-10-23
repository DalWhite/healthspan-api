using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.DependencyInjection;
using Services;

/// <summary>
/// Opt-in authorization filter for endpoints that accept a trainee/user filter.
/// Validates that the requested user belongs to the authenticated trainer
/// (or is the trainer themselves) BEFORE the action runs. A foreign trainee
/// yields 403 with no body and the action never executes, so no data is loaded
/// or exposed.
///
/// Usage:
///   [TraineeAccess]                       // looks for a "traineeId" then "userId" action parameter
///   [TraineeAccess("userId")]             // explicit parameter name
///
/// After validation the filter rewrites the matched action parameter with the
/// resolved target user id (empty/null becomes the trainer's own id), so the
/// action body can trust its parameter and does not re-resolve it.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public sealed class TraineeAccessAttribute : Attribute, IFilterFactory
{
    private readonly string? _parameterName;

    public TraineeAccessAttribute()
    {
    }

    public TraineeAccessAttribute(string parameterName)
    {
        _parameterName = parameterName;
    }

    public bool IsReusable => false;

    public IFilterMetadata CreateInstance(IServiceProvider serviceProvider)
        => ActivatorUtilities.CreateInstance<TraineeAccessFilter>(
            serviceProvider,
            _parameterName is null ? [] : new object[] { _parameterName });
}

/// <summary>
/// Action filter implementing <see cref="TraineeAccessAttribute"/>. Resolved via
/// DI (TypeFilterAttribute), so no global registration is required.
/// </summary>
public sealed class TraineeAccessFilter : IAsyncActionFilter
{
    /// <summary>
    /// Action parameter names probed, in order, when no explicit name is given.
    /// </summary>
    public static readonly IReadOnlyList<string> DefaultParameterNames = ["traineeId", "userId"];

    private readonly string? _parameterName;
    private readonly CurrentUserProvider _userProvider;
    private readonly TrainingAccessService _trainingAccessService;

    public TraineeAccessFilter(
        CurrentUserProvider userProvider,
        TrainingAccessService trainingAccessService)
        : this(null, userProvider, trainingAccessService)
    {
    }

    public TraineeAccessFilter(
        string? parameterName,
        CurrentUserProvider userProvider,
        TrainingAccessService trainingAccessService)
    {
        _parameterName = string.IsNullOrWhiteSpace(parameterName) ? null : parameterName;
        _userProvider = userProvider;
        _trainingAccessService = trainingAccessService;
    }

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var trainerUserId = _userProvider.GetCurrentUserId();
        if (string.IsNullOrWhiteSpace(trainerUserId))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var (parameterName, requestedUserId) = ResolveRequestedUser(context);
        var targetUserId = _trainingAccessService.ResolveTraineeUserId(trainerUserId, requestedUserId);
        if (targetUserId is null)
        {
            // Foreign trainee (or unknown user): 403, empty body, action never runs.
            context.Result = new ForbidResult();
            return;
        }

        if (parameterName is not null)
        {
            // Inject or overwrite: an optional parameter that was not supplied is
            // absent from ActionArguments (its C# default is applied at call time),
            // so the resolved id must be added for the action to trust its parameter.
            context.ActionArguments[parameterName] = targetUserId;
        }

        await next();
    }

    private (string? ParameterName, string? RequestedUserId) ResolveRequestedUser(ActionExecutingContext context)
    {
        var candidateNames = _parameterName is null
            ? DefaultParameterNames
            : new[] { _parameterName };

        foreach (var name in candidateNames)
        {
            if (context.ActionArguments.TryGetValue(name, out var value))
            {
                return (name, value as string);
            }
        }

        // Declared but not bound (optional parameter with a default value):
        // remember the name so the resolved id can be injected.
        foreach (var name in candidateNames)
        {
            if ((context.ActionDescriptor.Parameters ?? [])
                .Any(parameter => parameter.Name == name))
            {
                return (name, null);
            }
        }

        return (null, null);
    }
}
