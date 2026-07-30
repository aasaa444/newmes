using Mes.Domain.Identity;

namespace Mes.Infrastructure.IdentityAccess;

/// <summary>表示身份已确认，但不具备执行业务动作所需能力；异常携带可稳定映射的拒绝代码。</summary>
public sealed class CapabilityDeniedException(
    BusinessCapability capability,
    string reasonCode) : Exception("The current identity is not authorized for this action.")
{
    public BusinessCapability Capability { get; } = capability;

    public string ReasonCode { get; } = reasonCode;
}
