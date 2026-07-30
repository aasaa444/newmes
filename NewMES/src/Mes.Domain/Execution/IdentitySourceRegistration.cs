using Mes.Domain.Identity;

namespace Mes.Domain.Execution;

// 获准向 MES 分配身份的来源登记，包含授权依据和允许分配的标识类型。
public sealed class IdentitySourceRegistration
{
    public Guid Id { get; init; }

    public required string SourceSystem { get; init; }

    public IdentitySourceType SourceType { get; init; }

    public bool IsDemo { get; init; }

    public bool IsActive { get; set; }

    public required string AuthorizationEvidence { get; init; }

    public Guid AuthorizedCallerUserId { get; init; }

    public UserAccount? AuthorizedCallerUser { get; init; }

    public Guid RegisteredByUserId { get; init; }

    public UserAccount? RegisteredByUser { get; init; }

    public DateTimeOffset RegisteredAtUtc { get; init; }

    public ICollection<IdentitySourceIdentifierGrant> IdentifierGrants { get; } = [];
}
