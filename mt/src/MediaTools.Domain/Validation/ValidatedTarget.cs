using MediaTools.Domain.Models;

namespace MediaTools.Domain.Validation;

/// <summary>
/// Produced by TargetValidator when a path passes all validation rules.
/// Contains the inferred Kind and TargetMode so the caller doesn't need to re-derive them.
/// </summary>
public record ValidatedTarget(Kind Kind, TargetMode Mode, string AbsolutePath);
