namespace ClaimFlow.Platform.Hosting;

/// <summary>Base for errors that map to a specific HTTP status + a stable machine-readable code.</summary>
public class AppException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}

public sealed class BadRequestException(string code, string message) : AppException(400, code, message);
public sealed class UnauthorizedAppException(string message = "Authentication required.") : AppException(401, "UNAUTHENTICATED", message);
public sealed class ForbiddenException(string code, string message) : AppException(403, code, message);
public sealed class NotFoundException(string code, string message) : AppException(404, code, message);
/// <summary>State conflicts: invalid transition, already assigned, duplicates.</summary>
public sealed class ConflictException(string code, string message) : AppException(409, code, message);
/// <summary>Business rule violations: well-formed request, but the domain says no.</summary>
public sealed class RuleViolationException(string code, string message) : AppException(422, code, message);
public sealed class ServiceUnavailableException(string code, string message) : AppException(503, code, message);
