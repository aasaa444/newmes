using Mes.Domain.Identity;

namespace Mes.Identity.Tests;

/// <summary>锁定角色到业务能力的职责分离矩阵，防止后续改动无意扩大岗位权限。</summary>
public sealed class RoleCapabilityMatrixTests
{
    public static TheoryData<BusinessRole, BusinessCapability> CoreRoleCapabilities => new()
    {
        { BusinessRole.Planner, BusinessCapability.ProductionOrderManage },
        { BusinessRole.ProcessEngineer, BusinessCapability.ProcessDefinitionManage },
        { BusinessRole.ProcessEngineer, BusinessCapability.TestSpecificationRead },
        { BusinessRole.Operator, BusinessCapability.StationExecute },
        { BusinessRole.LineSupervisor, BusinessCapability.ProductionPauseRequest },
        { BusinessRole.QualityEngineer, BusinessCapability.QualityDispositionApprove },
        { BusinessRole.QualityEngineer, BusinessCapability.TestSpecificationApprove },
        { BusinessRole.QualityEngineer, BusinessCapability.TestSpecificationRead },
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
    [InlineData(BusinessRole.Planner, BusinessCapability.QualityDispositionApprove)]
    [InlineData(BusinessRole.ProcessEngineer, BusinessCapability.ProductionOrderManage)]
    [InlineData(BusinessRole.ProcessEngineer, BusinessCapability.TestSpecificationApprove)]
    [InlineData(BusinessRole.Operator, BusinessCapability.QualityDispositionApprove)]
    [InlineData(BusinessRole.LineSupervisor, BusinessCapability.QualityDispositionApprove)]
    [InlineData(BusinessRole.QualityEngineer, BusinessCapability.AccountManage)]
    [InlineData(BusinessRole.MaterialHandler, BusinessCapability.ProductionOrderManage)]
    [InlineData(BusinessRole.SystemAdministrator, BusinessCapability.QualityHoldRelease)]
    [InlineData(BusinessRole.OperationsManager, BusinessCapability.StationExecute)]
    public void EachBusinessRoleHasAnExplicitMajorDeniedPath(
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
