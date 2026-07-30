namespace Mes.Domain.Execution;

// 装配命令的只追加幂等回执，保存原命令哈希和首次业务结果。
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
