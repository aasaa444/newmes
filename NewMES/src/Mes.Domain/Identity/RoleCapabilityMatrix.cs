namespace Mes.Domain.Identity;

public sealed record CapabilityGrant(
    bool IsGranted,
    BusinessRole? GrantedByRole);

public static class RoleCapabilityMatrix
{
    private static readonly Dictionary<BusinessRole, HashSet<BusinessCapability>>
        CapabilitiesByRole = new()
        {
            [BusinessRole.Planner] = Set(
                BusinessCapability.IdentityContextRead,
                BusinessCapability.ProductionOrderRead,
                BusinessCapability.ProductionOrderManage),
            [BusinessRole.ProcessEngineer] = Set(
                BusinessCapability.IdentityContextRead,
                BusinessCapability.ProcessDefinitionRead,
                BusinessCapability.ProcessDefinitionManage,
                BusinessCapability.TestSpecificationApprove),
            [BusinessRole.Operator] = Set(
                BusinessCapability.IdentityContextRead,
                BusinessCapability.StationExecute,
                BusinessCapability.GenealogyRead,
                BusinessCapability.DefectReport),
            [BusinessRole.LineSupervisor] = Set(
                BusinessCapability.IdentityContextRead,
                BusinessCapability.ProductionOrderRead,
                BusinessCapability.LineExecutionRead,
                BusinessCapability.GenealogyRead,
                BusinessCapability.ProductionPauseRequest,
                BusinessCapability.QualityQueueRead),
            [BusinessRole.QualityEngineer] = Set(
                BusinessCapability.IdentityContextRead,
                BusinessCapability.QualityQueueRead,
                BusinessCapability.GenealogyRead,
                BusinessCapability.QualityDispositionApprove,
                BusinessCapability.QualityHoldRelease),
            [BusinessRole.MaterialHandler] = Set(
                BusinessCapability.IdentityContextRead,
                BusinessCapability.MaterialTransactionExecute,
                BusinessCapability.WarehouseHandoffExecute),
            [BusinessRole.SystemAdministrator] = Set(
                BusinessCapability.IdentityContextRead,
                BusinessCapability.AccountManage,
                BusinessCapability.SystemConfigurationManage,
                BusinessCapability.BusinessAuditRead),
            [BusinessRole.OperationsManager] = Set(
                BusinessCapability.IdentityContextRead,
                BusinessCapability.ProductionOrderRead,
                BusinessCapability.LineExecutionRead,
                BusinessCapability.GenealogyRead,
                BusinessCapability.QualityQueueRead,
                BusinessCapability.OperationsReportRead),
        };

    public static CapabilityGrant Authorize(
        IEnumerable<BusinessRole> roles,
        BusinessCapability capability)
    {
        ArgumentNullException.ThrowIfNull(roles);
        foreach (var role in roles.Distinct())
        {
            if (CapabilitiesByRole.TryGetValue(role, out var capabilities)
                && capabilities.Contains(capability))
            {
                return new CapabilityGrant(true, role);
            }
        }

        return new CapabilityGrant(false, null);
    }

    public static IReadOnlyCollection<BusinessCapability> GetEffectiveCapabilities(
        IEnumerable<BusinessRole> roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        return roles
            .Distinct()
            .Where(CapabilitiesByRole.ContainsKey)
            .SelectMany(role => CapabilitiesByRole[role])
            .Distinct()
            .Order()
            .ToArray();
    }

    private static HashSet<BusinessCapability> Set(
        params BusinessCapability[] capabilities) => capabilities.ToHashSet();
}
