namespace Mes.Domain.Quality;

// 质量保留表示是否阻断正常制造流转，不代表返工、报废或让步接收等处置结论。
public enum QualityHoldStatus
{
    Active,
    Released,
}
