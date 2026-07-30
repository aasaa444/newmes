namespace Mes.Domain.Identity;

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
