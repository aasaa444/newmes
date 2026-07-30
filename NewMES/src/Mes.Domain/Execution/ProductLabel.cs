namespace Mes.Domain.Execution;

// 一次标签打印事实；补打、换标和作废通过新记录及引用表达，而不是覆盖原打印。
public sealed class ProductLabel
{
    public Guid Id { get; init; }

    public Guid ProductIdentityId { get; init; }

    public ProductIdentity? ProductIdentity { get; init; }

    public required string TemplateVersion { get; init; }

    public required string Printer { get; init; }

    public ProductLabelStatus Status { get; set; }

    public Guid? ReplacesLabelId { get; init; }

    public ProductLabel? ReplacesLabel { get; init; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public byte[] Version { get; private set; } = [];
}
