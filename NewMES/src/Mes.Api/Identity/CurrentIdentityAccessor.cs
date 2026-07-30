using Mes.Infrastructure.IdentityAccess;

namespace Mes.Api.Identity;

/// <summary>保存当前 HTTP 请求的有效业务身份，生命周期限定为单个请求作用域。</summary>
public sealed class CurrentIdentityAccessor
{
    public EffectiveIdentity? Identity { get; internal set; }
}
