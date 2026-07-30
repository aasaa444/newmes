namespace Mes.Domain.Execution;

// START_WIP 命令的永久幂等回执，使设备重试不会重复投产或重复增加订单数量。
public sealed class StartWipCommandReceipt
{
    public Guid Id { get; init; }

    public required string SourceSystem { get; init; }

    public required string IdempotencyKey { get; init; }

    public required string CommandHash { get; init; }

    public Guid ProductIdentityId { get; init; }

    public Guid ProductionOrderId { get; init; }

    public required string SerialNumber { get; init; }

    public required string ProductionOrderNumber { get; init; }

    public required string OrderStatus { get; init; }

    public int StartedQuantity { get; init; }

    public required string IdentitySourceSystem { get; init; }

    public required string IdentitySourceType { get; init; }

    public bool IsDemo { get; init; }

    public string? NextOperationCode { get; init; }

    public DateTimeOffset CompletedAtUtc { get; init; }
}
