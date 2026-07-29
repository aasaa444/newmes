namespace Mes.Domain.Execution;

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
