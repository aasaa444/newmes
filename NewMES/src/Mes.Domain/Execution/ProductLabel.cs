namespace Mes.Domain.Execution;

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
