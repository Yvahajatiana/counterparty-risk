using CounterpartyRisk.Domain.Contracts.Results;

namespace CounterpartyRisk.Api.Http;

/// <summary>
/// Maps an Application <see cref="Result{TValue}"/> to an HTTP response. Failures become generic
/// RFC 9457 ProblemDetails carrying the stable error code only (SEC-210): no exception, no internal value.
/// </summary>
internal static class ResultHttpExtensions
{
    public static IResult ToHttpResult<TValue>(this Result<TValue> result)
    {
        ArgumentNullException.ThrowIfNull(result);
        return result.IsSuccess ? TypedResults.Ok(result.Value) : result.Error.ToProblem();
    }

    public static IResult ToProblem(this Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        var extensions = new Dictionary<string, object?> { ["code"] = error.Code };

        if (error is ValidationError validation)
        {
            extensions["errors"] = validation.Errors
                .Select(e => new { field = e.Field, code = e.Code, message = e.Message })
                .ToList();
        }

        var status = error.Type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status500InternalServerError,
        };

        // A technical failure keeps a generic title; expected failures expose their safe message.
        var detail = status == StatusCodes.Status500InternalServerError ? null : error.Message;
        return TypedResults.Problem(statusCode: status, detail: detail, extensions: extensions);
    }
}
