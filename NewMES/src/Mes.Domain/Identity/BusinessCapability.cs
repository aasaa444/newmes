namespace Mes.Domain.Identity;

// 服务端可执行的最小业务能力，用于避免仅凭页面入口或角色名称进行粗粒度授权。
public enum BusinessCapability
{
    IdentityContextRead,
    ProductionOrderRead,
    ProductionOrderManage,
    ProcessDefinitionRead,
    ProcessDefinitionManage,
    TestSpecificationRead,
    TestSpecificationApprove,
    StationExecute,
    DefectReport,
    LineExecutionRead,
    GenealogyRead,
    ProductionPauseRequest,
    QualityQueueRead,
    QualityDispositionApprove,
    QualityHoldRelease,
    MaterialTransactionExecute,
    WarehouseHandoffExecute,
    AccountManage,
    SystemConfigurationManage,
    BusinessAuditRead,
    OperationsReportRead,
}
