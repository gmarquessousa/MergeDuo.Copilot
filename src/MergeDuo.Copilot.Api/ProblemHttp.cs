namespace MergeDuo.Copilot.Api;

public static class ProblemHttp
{
    public static IResult Problem(int status, string code, string detail) =>
        TypedResults.Json(
            new
            {
                type = $"https://mergeduo.app/problems/{code}",
                title = code,
                status,
                detail,
                code
            },
            statusCode: status,
            contentType: "application/problem+json");

    public static Task WriteAsync(
        HttpContext context,
        int status,
        string code,
        string detail,
        CancellationToken cancellationToken)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";
        return context.Response.WriteAsJsonAsync(
            new
            {
                type = $"https://mergeduo.app/problems/{code}",
                title = code,
                status,
                detail,
                code
            },
            cancellationToken);
    }

    public static IResult DependencyUnavailable(string detail = "Copilot dependency unavailable.") =>
        Problem(StatusCodes.Status503ServiceUnavailable, "copilot_dependency_unavailable", detail);
}
