namespace Mes.Domain.Execution;

public sealed class IdentitySourceIdentifierGrant
{
    public Guid IdentitySourceRegistrationId { get; init; }

    public IdentitySourceRegistration? IdentitySourceRegistration { get; init; }

    public ControlledIdentifierType IdentifierType { get; init; }
}
