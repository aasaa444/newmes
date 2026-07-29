namespace Mes.Domain.Identity;

public enum BusinessCapability
{
    IdentityContextRead,
    ProductionOrderRead,
    ProductionOrderManage,
    ProcessDefinitionRead,
    ProcessDefinitionManage,
    TestSpecificationApprove,
    StationExecute,
    DefectReport,
    LineExecutionRead,
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
