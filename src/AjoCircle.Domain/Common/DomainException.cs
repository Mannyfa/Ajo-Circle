namespace AjoCircle.Domain.Common;

/// <summary>
/// A business rule was violated. Mapped to HTTP 422 (or 409 for <see cref="DomainConflictException"/>)
/// with a stable machine-readable <see cref="Code"/>.
/// </summary>
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

/// <summary>The request conflicts with current state (e.g. duplicate membership, stale terms version).</summary>
public sealed class DomainConflictException(string code, string message) : DomainException(code, message);

/// <summary>Validation failure with per-field messages.</summary>
public sealed class DomainValidationException(IReadOnlyDictionary<string, string[]> errors)
    : DomainException("validation_failed", "One or more fields are invalid.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;

    public static void ThrowIfAny(Dictionary<string, List<string>> errors)
    {
        if (errors.Count > 0)
            throw new DomainValidationException(errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
    }
}
