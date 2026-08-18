namespace VibeDesk.Application.Abstractions;

/// <summary>
/// Ambient identity for the running request/circuit. Implemented from the HTTP context on the server
/// and from the signed-in session on desktop/mobile hosts.
/// </summary>
public interface ICurrentUser
{
    Guid? Id { get; }
    string? Email { get; }
    string? DisplayName { get; }
    bool IsAuthenticated { get; }
    bool IsInRole(string role);

    /// <summary>Throws <see cref="UnauthorizedAccessException"/> when unauthenticated.</summary>
    Guid RequireId();
}

/// <summary>Thrown when an authenticated user lacks the role required for an operation.</summary>
public sealed class ForbiddenException(string message) : Exception(message);

/// <summary>Thrown when an entity referenced by id does not exist or is not visible to the caller.</summary>
public sealed class NotFoundException(string message) : Exception(message);

/// <summary>Thrown for input the caller could fix — surfaced as 400 by the API and as a toast in the UI.</summary>
public sealed class ValidationException(string message) : Exception(message);
