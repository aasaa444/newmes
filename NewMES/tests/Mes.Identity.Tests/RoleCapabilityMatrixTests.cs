using Mes.Domain.Identity;

namespace Mes.Identity.Tests;

public sealed class RoleCapabilityMatrixTests
{
    public static TheoryData<BusinessRole, BusinessCapability> CoreRoleCapabilities => new()
    {
        { BusinessRole.Planner, BusinessCapability.ProductionOrderManage },
        { BusinessRole.ProcessEngineer, BusinessCapability.ProcessDefinitionManage },
        { BusinessRole.Operator, BusinessCapability.StationExecute },
        { BusinessRole.LineSupervisor, BusinessCapability.ProductionPauseRequest },
        { BusinessRole.QualityEngineer, BusinessCapability.QualityDispositionApprove },
        { BusinessRole.MaterialHandler, BusinessCapability.MaterialTransactionExecute },
        { BusinessRole.SystemAdministrator, BusinessCapability.AccountManage },
        { BusinessRole.OperationsManager, BusinessCapability.OperationsReportRead },
    };

    [Theory]
    [MemberData(nameof(CoreRoleCapabilities))]
    public void EachBusinessRoleGrantsItsCoreCapability(
        BusinessRole role,
        BusinessCapability capability)
    {
        var grant = RoleCapabilityMatrix.Authorize([role], capability);

        Assert.True(grant.IsGranted);
        Assert.Equal(role, grant.GrantedByRole);
    }

    [Theory]
    [InlineData(BusinessRole.Operator, BusinessCapability.QualityDispositionApprove)]
    [InlineData(BusinessRole.LineSupervisor, BusinessCapability.QualityDispositionApprove)]
    [InlineData(BusinessRole.SystemAdministrator, BusinessCapability.QualityHoldRelease)]
    public void OperationalAndTechnicalRolesCannotApproveQualityDecisions(
        BusinessRole role,
        BusinessCapability capability)
    {
        var grant = RoleCapabilityMatrix.Authorize([role], capability);

        Assert.False(grant.IsGranted);
        Assert.Null(grant.GrantedByRole);
    }

    [Fact]
    public void CombinedRolesPreserveTheRoleThatAuthorizedTheAction()
    {
        var grant = RoleCapabilityMatrix.Authorize(
            [BusinessRole.Operator, BusinessRole.QualityEngineer],
            BusinessCapability.QualityDispositionApprove);

        Assert.True(grant.IsGranted);
        Assert.Equal(BusinessRole.QualityEngineer, grant.GrantedByRole);
    }
}
