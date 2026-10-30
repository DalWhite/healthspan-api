#nullable enable

using Microsoft.AspNetCore.Mvc;

internal static class ApiProblemResult
{
    public static ObjectResult Validation(
        string code,
        string detail,
        string title = "The request is not valid.",
        IDictionary<string, string[]>? errors = null)
        => Create(
            StatusCodes.Status400BadRequest,
            code,
            title,
            detail,
            errors);

    public static ObjectResult NotFound(
        string code,
        string detail)
        => Create(
            StatusCodes.Status404NotFound,
            code,
            "The requested health record was not found.",
            detail);

    public static ObjectResult Conflict(
        string code,
        string detail)
        => Create(
            StatusCodes.Status409Conflict,
            code,
            "The request conflicts with the current state.",
            detail);

    public static ObjectResult Forbidden(string code, string detail)
        => Create(
            StatusCodes.Status403Forbidden,
            code,
            "This action is not permitted on this instance.",
            detail);

    /// <summary>Shared 403 payload for the closed-registration gate (t_3798e8dd),
    /// used by both the /register middleware and the Google auth controller.</summary>
    public static ProblemDetails RegistrationClosedProblem()
    {
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status403Forbidden,
            Title = "Registration is closed on this instance.",
            Detail = "New accounts can only be created for pre-approved emails. If you already have an account, use sign-in.",
            Type = "https://httpstatuses.com/403"
        };
        problem.Extensions["code"] = "registration_closed";
        return problem;
    }

    public static ObjectResult Unexpected(string code)
        => Create(
            StatusCodes.Status500InternalServerError,
            code,
            "The health request could not be completed.",
            "Try again. If the problem continues, return later.");

    private static ObjectResult Create(
        int statusCode,
        string code,
        string title,
        string detail,
        IDictionary<string, string[]>? errors = null)
    {
        var problem = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Type =
                $"https://httpstatuses.com/{statusCode}"
        };
        problem.Extensions["code"] = code;
        if (errors is not null)
        {
            problem.Extensions["errors"] = errors;
        }

        return new ObjectResult(problem)
        {
            StatusCode = statusCode
        };
    }
}
