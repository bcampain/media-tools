namespace MediaTools.Domain.Validation;

/// <summary>
/// Represents the outcome of target path validation — either success with a ValidatedTarget,
/// or failure with a human-readable error message.
///
/// This is an example of the "Result pattern": instead of throwing an exception for expected
/// failures (like a bad user-supplied path), we return a typed result that forces the caller
/// to handle both cases. This keeps validation failures out of the exception-handling path
/// and makes unit testing straightforward.
/// </summary>
public record TargetValidationResult
{
    public bool IsSuccess { get; init; }
    public string? Error { get; init; }
    public ValidatedTarget? Target { get; init; }

    private TargetValidationResult() { }

    public static TargetValidationResult Ok(ValidatedTarget target) =>
        new() { IsSuccess = true, Target = target };

    public static TargetValidationResult Fail(string error) =>
        new() { IsSuccess = false, Error = error };
}
