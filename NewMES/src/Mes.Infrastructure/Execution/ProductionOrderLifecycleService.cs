using System.Data;
using Mes.Domain.Auditing;
using Mes.Domain.Execution;
using Mes.Domain.Identity;
using Mes.Infrastructure.IdentityAccess;
using Mes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Mes.Infrastructure.Execution;

/// <summary>
/// 执行生产订单的释放、开工、暂停、恢复和完工命令。
/// 服务负责权限、状态机、订单快照和审计的一致性，而不是让端点直接修改状态字段。
/// </summary>
public sealed class ProductionOrderLifecycleService(
    MesDbContext context,
    IdentityAccessService identityAccess,
    TimeProvider timeProvider)
{
    private const string ObjectType = "ProductionOrder";
    private readonly BusinessAuditWriter auditWriter = new(context, timeProvider);

    public async Task<ProductionOrderCommandResult> ReleaseAsync(
        EffectiveIdentity actor,
        Guid orderId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        const string action = "PRODUCTION_ORDER_RELEASE";
        await DemandManageAsync(actor, orderId, action, correlationId, cancellationToken);
        // Serializable 隔离保证并发释放只能生成一份订单快照；重复的同一释放命令返回既有结果。
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            var order = await context.ProductionOrders.SingleOrDefaultAsync(
                item => item.Id == orderId,
                cancellationToken);
            if (order is null)
            {
                await RejectAsync(actor, orderId, action, "PRODUCTION_ORDER_NOT_FOUND", correlationId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                throw Rejected(
                    "PRODUCTION_ORDER_NOT_FOUND",
                    "生产订单不存在；请刷新工作台后重试。",
                    404);
            }

            var existingSnapshot = await context.ProductionOrderExecutionSnapshots
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    snapshot => snapshot.ProductionOrderId == orderId,
                    cancellationToken);
            if (order.Status == ProductionOrderStatus.Released && existingSnapshot is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return Result(order, existingSnapshot.SnapshotVersion);
            }

            if (order.Status != ProductionOrderStatus.Received || existingSnapshot is not null)
            {
                await RejectTransitionAsync(actor, orderId, action, correlationId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                throw TransitionNotAllowed();
            }

            if (string.IsNullOrWhiteSpace(order.SourceSystem)
                || string.IsNullOrWhiteSpace(order.SourceReference)
                || string.IsNullOrWhiteSpace(order.SourceVersion))
            {
                await RejectAsync(actor, orderId, action, "PRODUCTION_ORDER_INCOMPLETE", correlationId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                throw Rejected(
                    "PRODUCTION_ORDER_INCOMPLETE",
                    "生产订单缺少可证明的来源系统、业务引用或来源版本，不能下达；请通过受控入站补齐新订单。",
                    422);
            }

            var template = await context.ProductExecutionTemplateVersions
                .AsNoTracking()
                .Where(item => item.MaterialId == order.MaterialId && item.IsApproved)
                .OrderByDescending(item => item.PublishedAtUtc)
                .ThenByDescending(item => item.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (template is null)
            {
                await RejectAsync(actor, orderId, action, "EXECUTION_TEMPLATE_NOT_FOUND", correlationId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                throw Rejected(
                    "EXECUTION_TEMPLATE_NOT_FOUND",
                    "该成品没有已批准且完整的执行模板；请由工艺工程师发布版本后再下达。",
                    422);
            }

            var now = timeProvider.GetUtcNow();
            // 释放时冻结当下已批准模板。此后所有工位执行都读取该快照，而不是读取可能已升级的模板。
            var snapshot = new ProductionOrderExecutionSnapshot
            {
                Id = Guid.NewGuid(),
                ProductionOrderId = order.Id,
                SourceTemplateId = template.Id,
                SnapshotVersion = template.Version,
                DefinitionJson = template.DefinitionJson,
                DefinitionHash = template.DefinitionHash,
                DefinitionHashAlgorithm = template.DefinitionHashAlgorithm,
                CreatedAtUtc = now,
                CreatedByUserId = actor.UserId,
            };
            order.Status = ProductionOrderStatus.Released;
            order.ReleasedAtUtc = now;
            context.ProductionOrderExecutionSnapshots.Add(snapshot);
            AppendSuccess(actor, orderId, action, correlationId);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result(order, snapshot.SnapshotVersion);
        });
    }

    public async Task<ProductionOrderCommandResult> ExecuteAsync(
        EffectiveIdentity actor,
        Guid orderId,
        ProductionOrderCommand command,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        if (command == ProductionOrderCommand.Release)
        {
            return await ReleaseAsync(actor, orderId, correlationId, cancellationToken);
        }

        var action = $"PRODUCTION_ORDER_{ToAction(command)}";
        await DemandManageAsync(actor, orderId, action, correlationId, cancellationToken);
        // 状态迁移和审计记录属于一个事务，任何一方失败都不能留下“已操作但无证据”的半成品。
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            var order = await context.ProductionOrders.SingleOrDefaultAsync(
                item => item.Id == orderId,
                cancellationToken);
            if (order is null)
            {
                await RejectAsync(actor, orderId, action, "PRODUCTION_ORDER_NOT_FOUND", correlationId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                throw Rejected(
                    "PRODUCTION_ORDER_NOT_FOUND",
                    "生产订单不存在；请刷新工作台后重试。",
                    404);
            }

            var rejection = Validate(order, command);
            if (rejection is not null)
            {
                await RejectAsync(actor, orderId, action, rejection.Code, correlationId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                throw rejection;
            }

            Apply(order, command, timeProvider.GetUtcNow());
            AppendSuccess(actor, orderId, action, correlationId);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            var snapshotVersion = await context.ProductionOrderExecutionSnapshots
                .AsNoTracking()
                .Where(snapshot => snapshot.ProductionOrderId == orderId)
                .Select(snapshot => snapshot.SnapshotVersion)
                .SingleOrDefaultAsync(cancellationToken);
            return Result(order, snapshotVersion);
        });
    }

    public async Task<string> ReadSnapshotJsonAsync(
        EffectiveIdentity actor,
        Guid orderId,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        await identityAccess.DemandCapabilityAsync(
            actor,
            BusinessCapability.ProductionOrderRead,
            "PRODUCTION_ORDER_SNAPSHOT_READ",
            ObjectType,
            orderId.ToString(),
            correlationId,
            cancellationToken);
        return await context.ProductionOrderExecutionSnapshots
            .AsNoTracking()
            .Where(snapshot => snapshot.ProductionOrderId == orderId)
            .Select(snapshot => snapshot.DefinitionJson)
            .SingleOrDefaultAsync(cancellationToken)
            ?? throw Rejected(
                "PRODUCTION_ORDER_SNAPSHOT_NOT_FOUND",
                "生产订单尚未下达，没有可读取的执行快照。",
                404);
    }

    private static ProductionOrderCommandRejectedException? Validate(
        ProductionOrder order,
        ProductionOrderCommand command)
    {
        if (command == ProductionOrderCommand.Cancel && order.StartedQuantity > 0)
        {
            return Rejected(
                "PRODUCTION_ORDER_CANNOT_CANCEL_AFTER_START",
                "该订单已经投产，不能取消；请暂停并通过受控完成或变更流程处理。",
                409);
        }

        if (command == ProductionOrderCommand.CompleteExecution
            && order.Status == ProductionOrderStatus.InProduction
            && (order.StartedQuantity <= 0
                || order.StartedQuantity != order.QualifiedQuantity + order.ScrappedQuantity))
        {
            return Rejected(
                "PRODUCTION_ORDER_QUANTITY_NOT_TERMINAL",
                "尚无投产记录或仍有在制数量，不能执行完成；请先处理全部已投产产品。",
                409);
        }

        if (command == ProductionOrderCommand.CompleteExecution
            && order.Status == ProductionOrderStatus.InProduction
            && order.OpenQualityHoldQuantity > 0)
        {
            return Rejected(
                "PRODUCTION_ORDER_QUALITY_HOLD_OPEN",
                "仍有未决质量保留，不能执行完成；请由质量工程师完成处置。",
                409);
        }

        if (command == ProductionOrderCommand.Close
            && order.Status == ProductionOrderStatus.ExecutionCompleted
            && (!order.WarehouseHandoffCompleted || !order.ErpReconciled))
        {
            return Rejected(
                "PRODUCTION_ORDER_CLOSURE_NOT_RECONCILED",
                "制造执行已完成，但仓储交接和 ERP 对账尚未同时完成，不能关闭订单。",
                409);
        }

        var allowed = command switch
        {
            ProductionOrderCommand.Pause => order.Status == ProductionOrderStatus.InProduction,
            ProductionOrderCommand.Resume => order.Status == ProductionOrderStatus.Paused,
            ProductionOrderCommand.Cancel => order.Status is
                ProductionOrderStatus.Received or ProductionOrderStatus.Released,
            ProductionOrderCommand.CompleteExecution =>
                order.Status == ProductionOrderStatus.InProduction,
            ProductionOrderCommand.Close =>
                order.Status == ProductionOrderStatus.ExecutionCompleted,
            _ => false,
        };
        return allowed ? null : TransitionNotAllowed();
    }

    private static void Apply(
        ProductionOrder order,
        ProductionOrderCommand command,
        DateTimeOffset now)
    {
        order.Status = command switch
        {
            ProductionOrderCommand.Pause => ProductionOrderStatus.Paused,
            ProductionOrderCommand.Resume => ProductionOrderStatus.InProduction,
            ProductionOrderCommand.Cancel => ProductionOrderStatus.Cancelled,
            ProductionOrderCommand.CompleteExecution => ProductionOrderStatus.ExecutionCompleted,
            ProductionOrderCommand.Close => ProductionOrderStatus.Closed,
            _ => order.Status,
        };
        if (command == ProductionOrderCommand.CompleteExecution)
        {
            order.ExecutionCompletedAtUtc = now;
        }
        else if (command == ProductionOrderCommand.Close)
        {
            order.ClosedAtUtc = now;
        }
    }

    private async Task DemandManageAsync(
        EffectiveIdentity actor,
        Guid orderId,
        string action,
        string correlationId,
        CancellationToken cancellationToken) =>
        await identityAccess.DemandCapabilityAsync(
            actor,
            BusinessCapability.ProductionOrderManage,
            action,
            ObjectType,
            orderId.ToString(),
            correlationId,
            cancellationToken);

    private void AppendSuccess(
        EffectiveIdentity actor,
        Guid orderId,
        string action,
        string correlationId) => auditWriter.Append(new BusinessAuditWrite(
            BusinessAuditActor.From(actor),
            BusinessRole.Planner,
            BusinessCapability.ProductionOrderManage,
            action,
            ObjectType,
            orderId.ToString(),
            BusinessAuditResult.Succeeded,
            null,
            correlationId));

    private async Task RejectTransitionAsync(
        EffectiveIdentity actor,
        Guid orderId,
        string action,
        string correlationId,
        CancellationToken cancellationToken) => await RejectAsync(
            actor,
            orderId,
            action,
            "PRODUCTION_ORDER_TRANSITION_NOT_ALLOWED",
            correlationId,
            cancellationToken);

    private async Task RejectAsync(
        EffectiveIdentity actor,
        Guid orderId,
        string action,
        string reasonCode,
        string correlationId,
        CancellationToken cancellationToken)
    {
        auditWriter.Append(new BusinessAuditWrite(
            BusinessAuditActor.From(actor),
            BusinessRole.Planner,
            BusinessCapability.ProductionOrderManage,
            action,
            ObjectType,
            orderId.ToString(),
            BusinessAuditResult.Denied,
            reasonCode,
            correlationId));
        await context.SaveChangesAsync(cancellationToken);
    }

    private static string ToAction(ProductionOrderCommand command) => command switch
    {
        ProductionOrderCommand.CompleteExecution => "EXECUTION_COMPLETE",
        _ => command.ToString().ToUpperInvariant(),
    };

    private static ProductionOrderCommandResult Result(
        ProductionOrder order,
        string? snapshotVersion) => new(order.Id, order.Status.ToString(), snapshotVersion);

    private static ProductionOrderCommandRejectedException TransitionNotAllowed() => Rejected(
        "PRODUCTION_ORDER_TRANSITION_NOT_ALLOWED",
        "当前订单状态不允许执行该命令；请刷新工作台并按可执行命令处理。",
        409);

    private static ProductionOrderCommandRejectedException Rejected(
        string code,
        string message,
        int statusCode) => new(code, message, statusCode);
}
