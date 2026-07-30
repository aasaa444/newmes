using Mes.Domain.Execution;
using Mes.Domain.Identity;
using Mes.Domain.MasterData;
using Mes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Mes.Infrastructure.Seeding;

/// <summary>
/// 显式装载可重复执行的非生产演示数据。固定标识便于测试和演示，但此初始化器绝不由生产 API 自动调用。
/// </summary>
public sealed class DemoDataInitializer(MesDbContext context)
{
    private static readonly Guid DemoUserId =
        Guid.Parse("d0000000-0000-0000-0000-000000000001");
    private static readonly Guid DemoMaterialId =
        Guid.Parse("d0000000-0000-0000-0000-000000000002");
    private static readonly Guid DemoOrderId =
        Guid.Parse("d0000000-0000-0000-0000-000000000003");

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var pending = await context.Database.GetPendingMigrationsAsync(cancellationToken);
        if (pending.Any())
        {
            throw new InvalidOperationException(
                "Demo data cannot be loaded before all database migrations are applied.");
        }

        var strategy = context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            // 初始化器独占迁移进程中的上下文；重试前清空跟踪，避免失败尝试留下的实体被重复添加。
            context.ChangeTracker.Clear();
            await using var transaction =
                await context.Database.BeginTransactionAsync(cancellationToken);

            if (!await context.UserAccounts.AnyAsync(
                    account => account.Username == "demo.operator",
                    cancellationToken))
            {
                context.UserAccounts.Add(new UserAccount
                {
                    Id = DemoUserId,
                    Username = "demo.operator",
                    DisplayName = "演示操作员",
                    IsActive = true,
                });
            }

            if (!await context.Materials.AnyAsync(
                    material => material.Code == "DEMO-ROUTER-01",
                    cancellationToken))
            {
                context.Materials.Add(new Material
                {
                    Id = DemoMaterialId,
                    Code = "DEMO-ROUTER-01",
                    Name = "非生产工业路由器演示产品",
                    BaseUnit = "EA",
                    TraceabilityMode = TraceabilityMode.Serial,
                    IsActive = true,
                });
            }

            await context.SaveChangesAsync(cancellationToken);

            if (!await context.ProductionOrders.AnyAsync(
                    order => order.OrderNumber == "DEMO-PO-0001",
                    cancellationToken))
            {
                context.ProductionOrders.Add(new ProductionOrder
                {
                    Id = DemoOrderId,
                    OrderNumber = "DEMO-PO-0001",
                    MaterialId = DemoMaterialId,
                    PlannedQuantity = 5,
                    Status = ProductionOrderStatus.Received,
                    CreatedAtUtc = new DateTimeOffset(2026, 7, 29, 0, 0, 0, TimeSpan.Zero),
                    SourceSystem = "DemoInitializer",
                    SourceReference = "explicit-non-production-seed",
                });
            }

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        });
    }
}
