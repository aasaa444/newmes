namespace Mes.Domain.Execution;

public sealed class AssemblyCommandReceipt
{
    public Guid Id { get; init; }

    public required string SourceSystem { get; init; }

    public required string IdempotencyKey { get; init; }

    public required string CommandType { get; init; }

    public required string CommandHash { get; init; }

    public required string CommandHashAlgorithm { get; init; }

    public Guid ProductIdentityId { get; init; }

    public required string ResultJson { get; init; }

    public DateTimeOffset CompletedAtUtc { get; init; }
}
