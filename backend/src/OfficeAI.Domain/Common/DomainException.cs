namespace OfficeAI.Domain.Common;

/// <summary>A business rule was violated. Mapped to HTTP 400/409 by the API.</summary>
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

public sealed class NotFoundException(string entity, Guid id)
    : Exception($"{entity} '{id}' was not found.")
{
    public string Entity { get; } = entity;
}

public sealed class ConcurrencyException()
    : Exception("The record was changed by someone else. Reload and try again.");

public sealed class ForbiddenException(string message) : Exception(message);
