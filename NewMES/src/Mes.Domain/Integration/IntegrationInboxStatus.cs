namespace Mes.Domain.Integration;

// 入站消息的业务处理结果；Rejected 也会永久保留，避免重复消息反复产生不同结果。
public enum IntegrationInboxStatus
{
    Accepted,
    Rejected,
}
