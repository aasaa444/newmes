namespace Mes.Infrastructure.Persistence;

public static class MesMigrationIds
{
    public const string InitialFoundation = "20260729000100_InitialFoundation";

    public const string EvolutionBaseline = "20260729000200_EvolutionBaseline";

    public const string CapabilityRolesAuditContext =
        "20260729000300_CapabilityRolesAuditContext";

    public const string IdempotentProductionOrderIngress =
        "20260729000400_IdempotentProductionOrderIngress";

    public const string PreserveErpIngressEvidence =
        "20260729140931_PreserveErpIngressEvidence";
}
