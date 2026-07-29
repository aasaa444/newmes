using Mes.Domain.Identity;

namespace Mes.Infrastructure.IdentityAccess;

public sealed class CapabilityDeniedException(
    BusinessCapability capability,
    string reasonCode) : Exception("The current identity is not authorized for this action.")
{
    public BusinessCapability Capability { get; } = capability;

    public string ReasonCode { get; } = reasonCode;
}
