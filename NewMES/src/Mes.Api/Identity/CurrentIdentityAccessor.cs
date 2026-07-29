using Mes.Infrastructure.IdentityAccess;

namespace Mes.Api.Identity;

public sealed class CurrentIdentityAccessor
{
    public EffectiveIdentity? Identity { get; internal set; }
}
