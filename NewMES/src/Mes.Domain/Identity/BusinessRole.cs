namespace Mes.Domain.Identity;

// 面向岗位的组合角色。授权最终按 BusinessCapability 判断，主角色只决定默认工作台语义。
public enum BusinessRole
{
    Planner,
    ProcessEngineer,
    Operator,
    LineSupervisor,
    QualityEngineer,
    MaterialHandler,
    SystemAdministrator,
    OperationsManager,
}
