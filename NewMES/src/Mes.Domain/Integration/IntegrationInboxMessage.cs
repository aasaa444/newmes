using Mes.Domain.Execution;

namespace Mes.Domain.Integration;

// ERP 入站收件箱事实。原始载荷、哈希和首次处理结果共同构成幂等重放依据。
public sealed class IntegrationInboxMessage
{
    public Guid Id { get; init; }

    public required string SourceSystem { get; init; }

    public required string MessageId { get; init; }

    public required string MessageType { get; init; }

    public string? BusinessKey { get; init; }

    public string? SourceVersion { get; init; }

    public string? ContractVersion { get; init; }

    public required string PayloadHash { get; init; }

    public required string PayloadHashAlgorithm { get; init; }

    // Null is reserved for records created before raw payload retention was introduced.
    public string? PayloadJson { get; init; }

    public IntegrationInboxStatus Status { get; init; }

    public required string ResultCode { get; init; }

    public required string ResultMessage { get; init; }

    public int HttpStatusCode { get; init; }

    public DateTimeOffset ReceivedAtUtc { get; init; }

    public DateTimeOffset ProcessedAtUtc { get; init; }

    public Guid? ProductionOrderId { get; init; }

    public ProductionOrder? ProductionOrder { get; init; }
}
