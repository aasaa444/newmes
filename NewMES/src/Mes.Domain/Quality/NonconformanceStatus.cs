namespace Mes.Domain.Quality;

// 不合格状态与质量保留状态分开演进；Ticket 11 只创建待处置记录。
public enum NonconformanceStatus
{
    Open,
    Dispositioned,
}
