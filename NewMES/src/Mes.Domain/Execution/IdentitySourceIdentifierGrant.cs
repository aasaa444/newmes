namespace Mes.Domain.Execution;

// 身份来源与可分配标识类型的白名单，防止某个接口越权生成未获批准的标识。
public sealed class IdentitySourceIdentifierGrant
{
    public Guid IdentitySourceRegistrationId { get; init; }

    public IdentitySourceRegistration? IdentitySourceRegistration { get; init; }

    public ControlledIdentifierType IdentifierType { get; init; }
}
