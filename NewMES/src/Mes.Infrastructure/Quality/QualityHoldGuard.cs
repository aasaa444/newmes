using Mes.Domain.Quality;
using Mes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Mes.Infrastructure.Quality;

/// <summary>集中判断产品是否处于有效质量保留，供各工位在写入任何新事实前执行同一门禁。</summary>
internal static class QualityHoldGuard
{
    public static Task<bool> IsActiveAsync(
        MesDbContext context,
        Guid productIdentityId,
        CancellationToken cancellationToken) => context.QualityHolds
        .AsNoTracking()
        .AnyAsync(
            item => item.ProductIdentityId == productIdentityId
                && item.Status == QualityHoldStatus.Active,
            cancellationToken);
}
