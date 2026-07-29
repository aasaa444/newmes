using Mes.Domain.Identity;

namespace Mes.Domain.Execution;

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
