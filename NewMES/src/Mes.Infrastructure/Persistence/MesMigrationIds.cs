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

    public const string PreserveRejectedIngressGaps =
        "20260729150000_PreserveRejectedIngressGaps";

    public const string OrderReleaseSnapshotLifecycle =
        "20260729160000_OrderReleaseSnapshotLifecycle";

    public const string LineSideMaterialTransactionLedger =
        "20260729170000_LineSideMaterialTransactionLedger";

    public const string ControlledIdentityLabelStartWip =
        "20260729180000_ControlledIdentityLabelStartWip";

    public const string AuthorizedIdentitySourcesAndReceipts =
        "20260729190000_AuthorizedIdentitySourcesAndReceipts";

    public const string AssemblyBindingConsumption =
        "20260729200000_AssemblyBindingConsumption";

    public const string FirmwareConfigurationExecution =
        "20260730021808_FirmwareConfigurationExecution";
}
