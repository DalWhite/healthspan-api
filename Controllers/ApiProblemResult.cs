#nullable enable

using Microsoft.AspNetCore.Mvc;

internal static class ApiProblemResult
{
    public static ObjectResult Validation(
        string code,
        string detail,
        string title = "The request is not valid.")
        => Create(
            StatusCodes.Status400BadRequest,
            code,
            title,
            detail);

    public static ObjectResult NotFound(
        string code,
        string detail)
        => Create(
            StatusCodes.Status404NotFound,
            code,
            "The requested health record was not found.",
            detail);

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
        string detail)
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

        return new ObjectResult(problem)
        {
            StatusCode = statusCode
        };
    }
}
