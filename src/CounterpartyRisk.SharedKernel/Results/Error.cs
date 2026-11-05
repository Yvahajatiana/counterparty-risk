using System.Diagnostics.CodeAnalysis;

namespace CounterpartyRisk.SharedKernel.Results;

/// <summary>
/// Category of an expected failure. The API layer maps it to an HTTP status
/// (ProblemDetails) without leaking internal details.
/// </summary>
public enum ErrorType
{
    Failure = 0,
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
    Unauthorized = 4,
    Forbidden = 5,
}

/// <summary>
/// Expected business failure. <see cref="Message"/> must be safe to display:
/// never put secrets, tokens, amounts or counterparty identifiers in it.
/// </summary>
/// <param name="Code">Stable, machine-readable code (e.g. "portfolio.netting_set_not_found").</param>
/// <param name="Message">Human-readable, non-sensitive description.</param>
/// <param name="Type">Error category.</param>
[SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "C#-only codebase; Error is the ADR-001 name.")]
public record Error(string Code, string Message, ErrorType Type)
{
    public static Error Failure(string code, string message) => new(code, message, ErrorType.Failure);

    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);
}

/// <summary>
/// Validation failure carrying one entry per invalid field.
/// </summary>
public sealed record ValidationError : Error
{
    public ValidationError(IReadOnlyList<FieldError> errors)
        : base("validation.failed", "One or more validation errors occurred.", ErrorType.Validation)
    {
        Errors = errors;
    }

    public IReadOnlyList<FieldError> Errors { get; }
}

/// <summary>
/// A single invalid field. The attempted value is deliberately not stored (may be sensitive).
/// </summary>
public sealed record FieldError(string Field, string Code, string Message);
