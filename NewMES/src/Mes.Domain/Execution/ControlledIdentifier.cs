namespace Mes.Domain.Execution;

// 与成品绑定的受控标识，始终保存其来源系统和来源依据，不能只保留最终字符串。
public sealed class ControlledIdentifier
{
    public Guid Id { get; init; }

    public Guid ProductIdentityId { get; init; }

    public ProductIdentity? ProductIdentity { get; init; }

    public ControlledIdentifierType Type { get; init; }

    public required string Value { get; init; }

    public IdentitySourceType SourceType { get; init; }

    public required string SourceSystem { get; init; }

    public required string SourceReference { get; init; }

    public bool IsDemo { get; init; }
}
