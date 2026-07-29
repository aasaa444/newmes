using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mes.Domain.Auditing;
using Mes.Domain.Execution;
using Mes.Domain.Identity;
using Mes.Domain.Materials;
using Mes.Domain.MasterData;
using Mes.Infrastructure.Execution;
using Mes.Infrastructure.IdentityAccess;
using Mes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Mes.Infrastructure.Materials;

public sealed class MaterialTransactionService(
    MesDbContext context,
    IdentityAccessService identityAccess,
    TimeProvider timeProvider)
{
    private const string ContractVersion = "1.0";
    private const string HashAlgorithm = "SHA-256-JSON-V1";
    private const string ObjectType = "MaterialTransaction";
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private readonly BusinessAuditWriter auditWriter = new(context, timeProvider);

    public Task<MaterialTransactionResult> TransferAsync(
        EffectiveIdentity actor,
        LineSideTransferRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateTransfer(request);
        var proposal = validation is null
            ? new ProposedTransaction(
                MaterialTransactionType.LineSideTransfer,
                Normalize(request.SourceSystem),
                Normalize(request.IdempotencyKey),
                Normalize(request.MaterialCode),
                Normalize(request.LotNumber),
                request.Quantity,
                NormalizeUnit(request.Unit),
                null,
                request.Quantity,
                0,
                0,
                Normalize(request.SourceDocumentType),
                Normalize(request.SourceDocumentNumber),
                Normalize(request.FromParty),
                Normalize(request.ToParty),
                null,
                request.OccurredAtUtc.ToUniversalTime(),
                Hash(request))
            : null;
        return ExecuteAsync(actor, proposal, validation, correlationId, cancellationToken);
    }

    public Task<MaterialTransactionResult> IssueAsync(
        EffectiveIdentity actor,
        OrderMaterialRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateOrderRequest(request, requireReason: false);
        var proposal = validation is null
            ? new ProposedTransaction(
                MaterialTransactionType.OrderIssue,
                Normalize(request.SourceSystem),
                Normalize(request.IdempotencyKey),
                Normalize(request.MaterialCode),
                Normalize(request.LotNumber),
                request.Quantity,
                NormalizeUnit(request.Unit),
                request.ProductionOrderId,
                -request.Quantity,
                request.Quantity,
                request.Quantity,
                "ORDER_ISSUE",
                NormalizeNullable(request.SourceDocumentNumber),
                null,
                null,
                null,
                request.OccurredAtUtc.ToUniversalTime(),
                Hash(request))
            : null;
        return ExecuteAsync(actor, proposal, validation, correlationId, cancellationToken);
    }

    public Task<MaterialTransactionResult> ReturnAsync(
        EffectiveIdentity actor,
        OrderMaterialRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateOrderRequest(request, requireReason: true);
        var proposal = validation is null
            ? new ProposedTransaction(
                MaterialTransactionType.OrderReturn,
                Normalize(request.SourceSystem),
                Normalize(request.IdempotencyKey),
                Normalize(request.MaterialCode),
                Normalize(request.LotNumber),
                request.Quantity,
                NormalizeUnit(request.Unit),
                request.ProductionOrderId,
                request.Quantity,
                -request.Quantity,
                -request.Quantity,
                "ORDER_RETURN",
                NormalizeNullable(request.SourceDocumentNumber),
                null,
                null,
                Normalize(request.Reason),
                request.OccurredAtUtc.ToUniversalTime(),
                Hash(request))
            : null;
        return ExecuteAsync(actor, proposal, validation, correlationId, cancellationToken);
    }

    public Task<MaterialTransactionResult> AdjustAsync(
        EffectiveIdentity actor,
        MaterialAdjustmentRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var validation = ValidateAdjustment(request);
        var proposal = validation is null
            ? new ProposedTransaction(
                MaterialTransactionType.Adjustment,
                Normalize(request.SourceSystem),
                Normalize(request.IdempotencyKey),
                Normalize(request.MaterialCode),
                Normalize(request.LotNumber),
                Math.Abs(request.QuantityDelta),
                NormalizeUnit(request.Unit),
                null,
                request.QuantityDelta,
                0,
                0,
                "MATERIAL_ADJUSTMENT",
                null,
                null,
                null,
                Normalize(request.Reason),
                request.OccurredAtUtc.ToUniversalTime(),
                Hash(request))
            : null;
        return ExecuteAsync(actor, proposal, validation, correlationId, cancellationToken);
    }

    public async Task<MaterialTransactionResult> ReverseAsync(
        EffectiveIdentity actor,
        Guid originalTransactionId,
        MaterialReversalRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        const string action = "MATERIAL_TRANSACTION_REVERSE";
        await DemandExecuteAsync(actor, action, originalTransactionId.ToString(), correlationId, cancellationToken);
        var validation = ValidateReversal(request, originalTransactionId);
        if (validation is not null)
        {
            await RejectAsync(actor, action, originalTransactionId.ToString(), validation.Code, correlationId, cancellationToken);
            throw validation;
        }

        var sourceSystem = Normalize(request.SourceSystem);
        var idempotencyKey = Normalize(request.IdempotencyKey);
        var commandHash = Hash(new { originalTransactionId, request });
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            MaterialTransactionResult? replay;
            try
            {
                replay = await FindReplayAsync(
                    actor,
                    action,
                    sourceSystem,
                    idempotencyKey,
                    commandHash,
                    correlationId,
                    cancellationToken);
            }
            catch (MaterialTransactionRejectedException)
            {
                await transaction.CommitAsync(cancellationToken);
                throw;
            }
            if (replay is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return replay;
            }

            var original = await context.MaterialTransactions
                .AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == originalTransactionId, cancellationToken);
            if (original is null)
            {
                var error = Rejected(
                    "MATERIAL_TRANSACTION_NOT_FOUND",
                    "原物料事务不存在；请刷新工作台并核对事务编号。",
                    404);
                await RejectAsync(actor, action, originalTransactionId.ToString(), error.Code, correlationId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                throw error;
            }

            if (original.TransactionType is MaterialTransactionType.Reversal
                or MaterialTransactionType.Consumption
                || await context.MaterialTransactions.AnyAsync(
                    item => item.ReversesTransactionId == originalTransactionId,
                    cancellationToken))
            {
                var error = Rejected(
                    "MATERIAL_TRANSACTION_NOT_REVERSIBLE",
                    "该物料事务不可冲正或已经冲正；不得覆盖、删除或重复冲正原记录。",
                    409);
                await RejectAsync(actor, action, originalTransactionId.ToString(), error.Code, correlationId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                throw error;
            }

            var proposal = new ProposedTransaction(
                MaterialTransactionType.Reversal,
                sourceSystem,
                idempotencyKey,
                await context.Materials
                    .Where(material => material.Id == original.MaterialId)
                    .Select(material => material.Code)
                    .SingleAsync(cancellationToken),
                original.LotNumber,
                original.Quantity,
                original.Unit,
                original.ProductionOrderId,
                -original.LineSideQuantityDelta,
                -original.OrderAvailableQuantityDelta,
                -original.OrderIssuedQuantityDelta,
                "REVERSAL",
                original.Id.ToString(),
                original.ToParty,
                original.FromParty,
                Normalize(request.Reason),
                request.OccurredAtUtc.ToUniversalTime(),
                commandHash,
                original.Id);
            MaterialTransactionResult result;
            try
            {
                result = await ValidateAndAppendAsync(
                    actor,
                    action,
                    proposal,
                    correlationId,
                    cancellationToken);
            }
            catch (MaterialTransactionRejectedException)
            {
                await transaction.CommitAsync(cancellationToken);
                throw;
            }
            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }

    private async Task<MaterialTransactionResult> ExecuteAsync(
        EffectiveIdentity actor,
        ProposedTransaction? proposal,
        MaterialTransactionRejectedException? validation,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var action = ActionFor(proposal?.TransactionType);
        var objectId = proposal is null
            ? "invalid-request"
            : $"{proposal.SourceSystem}:{proposal.IdempotencyKey}";
        await DemandExecuteAsync(actor, action, objectId, correlationId, cancellationToken);
        if (validation is not null || proposal is null)
        {
            var error = validation ?? InvalidRequest();
            await RejectAsync(actor, action, objectId, error.Code, correlationId, cancellationToken);
            throw error;
        }

        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            MaterialTransactionResult? replay;
            try
            {
                replay = await FindReplayAsync(
                    actor,
                    action,
                    proposal.SourceSystem,
                    proposal.IdempotencyKey,
                    proposal.CommandHash,
                    correlationId,
                    cancellationToken);
            }
            catch (MaterialTransactionRejectedException)
            {
                await transaction.CommitAsync(cancellationToken);
                throw;
            }
            if (replay is not null)
            {
                await transaction.CommitAsync(cancellationToken);
                return replay;
            }

            MaterialTransactionResult result;
            try
            {
                result = await ValidateAndAppendAsync(
                    actor,
                    action,
                    proposal,
                    correlationId,
                    cancellationToken);
            }
            catch (MaterialTransactionRejectedException)
            {
                await transaction.CommitAsync(cancellationToken);
                throw;
            }
            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }

    private async Task<MaterialTransactionResult> ValidateAndAppendAsync(
        EffectiveIdentity actor,
        string action,
        ProposedTransaction proposal,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var material = await context.Materials.SingleOrDefaultAsync(
            item => item.Code == proposal.MaterialCode && item.IsActive,
            cancellationToken);
        if (material is null)
        {
            throw await RejectBusinessAsync(
                actor,
                action,
                proposal,
                "MATERIAL_NOT_FOUND",
                "物料不存在或未启用；请先完成 ERP/MES 主数据准备。",
                422,
                correlationId,
                cancellationToken);
        }

        if (string.IsNullOrWhiteSpace(material.BaseUnit))
        {
            throw await RejectBusinessAsync(
                actor,
                action,
                proposal,
                "MATERIAL_UNIT_UNPROVEN",
                "物料基础单位无法证明，不能记账；请先通过受控主数据同步补齐单位。",
                422,
                correlationId,
                cancellationToken);
        }

        if (!string.Equals(material.BaseUnit, proposal.Unit, StringComparison.Ordinal))
        {
            throw await RejectBusinessAsync(
                actor,
                action,
                proposal,
                "MATERIAL_UNIT_MISMATCH",
                $"计量单位必须使用物料基础单位 {material.BaseUnit}；请修正后重试。",
                422,
                correlationId,
                cancellationToken);
        }

        ProductionOrder? order = null;
        decimal? maximumIssueQuantity = null;
        if (proposal.ProductionOrderId is not null)
        {
            order = await context.ProductionOrders.SingleOrDefaultAsync(
                item => item.Id == proposal.ProductionOrderId,
                cancellationToken);
            if (order is null)
            {
                throw await RejectBusinessAsync(
                    actor,
                    action,
                    proposal,
                    "PRODUCTION_ORDER_NOT_FOUND",
                    "生产订单不存在；请刷新工作台并核对订单。",
                    404,
                    correlationId,
                    cancellationToken);
            }

            if (!IsOrderStateAllowed(order.Status, proposal.TransactionType))
            {
                throw await RejectBusinessAsync(
                    actor,
                    action,
                    proposal,
                    "MATERIAL_ORDER_STATE_NOT_ALLOWED",
                    "当前订单状态不允许发料、退料或冲正；请先确认订单已经下达且尚未关闭。",
                    409,
                    correlationId,
                    cancellationToken);
            }

            try
            {
                maximumIssueQuantity = await ReadMaximumIssueQuantityAsync(
                    order,
                    material.Code,
                    proposal.Unit,
                    cancellationToken);
            }
            catch (MaterialTransactionRejectedException error)
            {
                await RejectAsync(
                    actor,
                    action,
                    $"{proposal.SourceSystem}:{proposal.IdempotencyKey}",
                    error.Code,
                    correlationId,
                    cancellationToken);
                throw;
            }
        }

        var lineSideBalance = await context.MaterialTransactions
            .Where(item => item.MaterialId == material.Id && item.LotNumber == proposal.LotNumber)
            .SumAsync(item => (decimal?)item.LineSideQuantityDelta, cancellationToken) ?? 0;
        var lineSideAfter = lineSideBalance + proposal.LineSideDelta;
        if (lineSideAfter < 0)
        {
            throw await RejectBusinessAsync(
                actor,
                action,
                proposal,
                "LINE_SIDE_INVENTORY_INSUFFICIENT",
                $"线边物料 {material.Code} / Lot {proposal.LotNumber} 可用量为 {lineSideBalance} {proposal.Unit}，不能形成负库存。",
                409,
                correlationId,
                cancellationToken);
        }

        decimal? orderAvailableAfter = null;
        if (order is not null)
        {
            var orderAvailable = await context.MaterialTransactions
                .Where(item => item.ProductionOrderId == order.Id
                    && item.MaterialId == material.Id
                    && item.LotNumber == proposal.LotNumber)
                .SumAsync(item => (decimal?)item.OrderAvailableQuantityDelta, cancellationToken) ?? 0;
            orderAvailableAfter = orderAvailable + proposal.OrderAvailableDelta;
            if (orderAvailableAfter < 0)
            {
                throw await RejectBusinessAsync(
                    actor,
                    action,
                    proposal,
                    "ORDER_MATERIAL_INSUFFICIENT",
                    $"订单 {order.OrderNumber} 的物料 {material.Code} / Lot {proposal.LotNumber} 可用量为 {orderAvailable} {proposal.Unit}，不能退料或冲正。",
                    409,
                    correlationId,
                    cancellationToken);
            }

            var netIssued = await context.MaterialTransactions
                .Where(item => item.ProductionOrderId == order.Id && item.MaterialId == material.Id)
                .SumAsync(item => (decimal?)item.OrderIssuedQuantityDelta, cancellationToken) ?? 0;
            var netIssuedAfter = netIssued + proposal.OrderIssuedDelta;
            if (netIssuedAfter < 0 || netIssuedAfter > maximumIssueQuantity)
            {
                throw await RejectBusinessAsync(
                    actor,
                    action,
                    proposal,
                    "ORDER_MATERIAL_OVER_ISSUE",
                    $"订单 {order.OrderNumber} 的物料 {material.Code} 净发料量不能超过快照 BOM 需求 {maximumIssueQuantity} {proposal.Unit}。",
                    409,
                    correlationId,
                    cancellationToken);
            }
        }

        var recordedAt = timeProvider.GetUtcNow();
        var entity = new MaterialTransaction
        {
            Id = Guid.NewGuid(),
            TransactionType = proposal.TransactionType,
            MaterialId = material.Id,
            ProductionOrderId = proposal.ProductionOrderId,
            LotNumber = proposal.LotNumber,
            Quantity = proposal.Quantity,
            Unit = proposal.Unit,
            LineSideQuantityDelta = proposal.LineSideDelta,
            OrderAvailableQuantityDelta = proposal.OrderAvailableDelta,
            OrderIssuedQuantityDelta = proposal.OrderIssuedDelta,
            SourceSystem = proposal.SourceSystem,
            IdempotencyKey = proposal.IdempotencyKey,
            SourceDocumentType = proposal.SourceDocumentType,
            SourceDocumentNumber = proposal.SourceDocumentNumber,
            FromParty = proposal.FromParty,
            ToParty = proposal.ToParty,
            ReversesTransactionId = proposal.ReversesTransactionId,
            Reason = proposal.Reason,
            ActorUserId = actor.UserId,
            OccurredAtUtc = proposal.OccurredAtUtc,
            RecordedAtUtc = recordedAt,
            CommandHash = proposal.CommandHash,
            CommandHashAlgorithm = HashAlgorithm,
            CorrelationId = correlationId,
            LineSideBalanceAfter = lineSideAfter,
            OrderAvailableBalanceAfter = orderAvailableAfter,
        };
        context.MaterialTransactions.Add(entity);
        AppendAudit(actor, action, entity.Id.ToString(), BusinessAuditResult.Succeeded, null, correlationId);
        await context.SaveChangesAsync(cancellationToken);
        return Result(entity, isReplay: false);
    }

    private async Task<decimal> ReadMaximumIssueQuantityAsync(
        ProductionOrder order,
        string materialCode,
        string unit,
        CancellationToken cancellationToken)
    {
        var definitionJson = await context.ProductionOrderExecutionSnapshots
            .Where(snapshot => snapshot.ProductionOrderId == order.Id)
            .Select(snapshot => snapshot.DefinitionJson)
            .SingleOrDefaultAsync(cancellationToken);
        var definition = definitionJson is null
            ? null
            : JsonSerializer.Deserialize<ExecutionTemplateDefinition>(definitionJson, WebJson);
        var components = definition?.Bom.Components
            .Where(item => string.Equals(
                item.MaterialCode,
                materialCode,
                StringComparison.Ordinal))
            .ToArray() ?? [];
        if (components.Length == 0)
        {
            throw Rejected(
                "ORDER_MATERIAL_NOT_IN_SNAPSHOT",
                "该物料不在订单冻结的 BOM 中，不能发料或退料。",
                422);
        }

        if (components.Length > 1)
        {
            throw Rejected(
                "ORDER_MATERIAL_SNAPSHOT_AMBIGUOUS",
                "订单冻结 BOM 对同一物料存在多行定义，无法可靠计算发料上限；请通过受控模板版本修正。",
                422);
        }

        var component = components[0];

        if (!string.Equals(NormalizeUnit(component.Unit), unit, StringComparison.Ordinal))
        {
            throw Rejected(
                "ORDER_MATERIAL_UNIT_MISMATCH",
                "请求单位与订单冻结 BOM 的计量单位不一致；请核对执行快照。",
                422);
        }

        return order.PlannedQuantity * component.QuantityPer;
    }

    private async Task<MaterialTransactionResult?> FindReplayAsync(
        EffectiveIdentity actor,
        string action,
        string sourceSystem,
        string idempotencyKey,
        string commandHash,
        string correlationId,
        CancellationToken cancellationToken)
    {
        var existing = await context.MaterialTransactions.AsNoTracking().SingleOrDefaultAsync(
            item => item.SourceSystem == sourceSystem && item.IdempotencyKey == idempotencyKey,
            cancellationToken);
        if (existing is null)
        {
            return null;
        }

        if (string.Equals(existing.CommandHash, commandHash, StringComparison.Ordinal)
            && string.Equals(existing.CommandHashAlgorithm, HashAlgorithm, StringComparison.Ordinal))
        {
            return Result(existing, isReplay: true);
        }

        var error = Rejected(
            "MATERIAL_IDEMPOTENCY_CONFLICT",
            "相同来源和幂等键已经对应另一份物料事务；请核对原请求，不得覆盖既有账。",
            409);
        await RejectAsync(
            actor,
            action,
            $"{sourceSystem}:{idempotencyKey}",
            error.Code,
            correlationId,
            cancellationToken);
        throw error;
    }

    private async Task<MaterialTransactionRejectedException> RejectBusinessAsync(
        EffectiveIdentity actor,
        string action,
        ProposedTransaction proposal,
        string code,
        string message,
        int statusCode,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await RejectAsync(
            actor,
            action,
            $"{proposal.SourceSystem}:{proposal.IdempotencyKey}",
            code,
            correlationId,
            cancellationToken);
        return Rejected(code, message, statusCode);
    }

    private async Task DemandExecuteAsync(
        EffectiveIdentity actor,
        string action,
        string objectId,
        string correlationId,
        CancellationToken cancellationToken) => await identityAccess.DemandCapabilityAsync(
            actor,
            BusinessCapability.MaterialTransactionExecute,
            action,
            ObjectType,
            objectId,
            correlationId,
            cancellationToken);

    private async Task RejectAsync(
        EffectiveIdentity actor,
        string action,
        string objectId,
        string reasonCode,
        string correlationId,
        CancellationToken cancellationToken)
    {
        AppendAudit(actor, action, objectId, BusinessAuditResult.Denied, reasonCode, correlationId);
        await context.SaveChangesAsync(cancellationToken);
    }

    private void AppendAudit(
        EffectiveIdentity actor,
        string action,
        string objectId,
        BusinessAuditResult result,
        string? reasonCode,
        string correlationId) => auditWriter.Append(new BusinessAuditWrite(
            BusinessAuditActor.From(actor),
            BusinessRole.MaterialHandler,
            BusinessCapability.MaterialTransactionExecute,
            action,
            ObjectType,
            objectId,
            result,
            reasonCode,
            correlationId));

    private static MaterialTransactionRejectedException? ValidateTransfer(
        LineSideTransferRequest request) =>
        !CommonValid(
            request.ContractVersion,
            request.SourceSystem,
            request.IdempotencyKey,
            request.MaterialCode,
            request.LotNumber,
            request.Quantity,
            request.Unit,
            request.OccurredAtUtc)
        || !Required(request.SourceDocumentType, 80)
        || !Required(request.SourceDocumentNumber, 160)
        || !Required(request.FromParty, 160)
        || !Required(request.ToParty, 160)
            ? InvalidRequest()
            : null;

    private static MaterialTransactionRejectedException? ValidateOrderRequest(
        OrderMaterialRequest request,
        bool requireReason) =>
        !CommonValid(
            request.ContractVersion,
            request.SourceSystem,
            request.IdempotencyKey,
            request.MaterialCode,
            request.LotNumber,
            request.Quantity,
            request.Unit,
            request.OccurredAtUtc)
        || request.ProductionOrderId == Guid.Empty
        || !Optional(request.SourceDocumentNumber, 160)
        || !Optional(request.Reason, 400)
        || (requireReason && !Required(request.Reason, 400))
            ? InvalidRequest()
            : null;

    private static MaterialTransactionRejectedException? ValidateAdjustment(
        MaterialAdjustmentRequest request) =>
        request.QuantityDelta == decimal.MinValue
        || !CommonValid(
            request.ContractVersion,
            request.SourceSystem,
            request.IdempotencyKey,
            request.MaterialCode,
            request.LotNumber,
            Math.Abs(request.QuantityDelta),
            request.Unit,
            request.OccurredAtUtc)
        || request.QuantityDelta == 0
        || !Required(request.Reason, 400)
            ? InvalidRequest()
            : null;

    private static MaterialTransactionRejectedException? ValidateReversal(
        MaterialReversalRequest request,
        Guid originalTransactionId) =>
        originalTransactionId == Guid.Empty
        || !string.Equals(request.ContractVersion, ContractVersion, StringComparison.Ordinal)
        || !Required(request.SourceSystem, 80)
        || !Required(request.IdempotencyKey, 120)
        || !Required(request.Reason, 400)
        || request.OccurredAtUtc == default
            ? InvalidRequest()
            : null;

    private static bool CommonValid(
        string? contractVersion,
        string? sourceSystem,
        string? idempotencyKey,
        string? materialCode,
        string? lotNumber,
        decimal quantity,
        string? unit,
        DateTimeOffset occurredAtUtc) =>
        string.Equals(contractVersion, ContractVersion, StringComparison.Ordinal)
        && Required(sourceSystem, 80)
        && Required(idempotencyKey, 120)
        && Required(materialCode, 80)
        && Required(lotNumber, 120)
        && Required(unit, 24)
        && quantity > 0
        && quantity <= 999_999_999_999m
        && decimal.Round(quantity, 6) == quantity
        && occurredAtUtc != default;

    private static bool Required(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maximumLength;

    private static bool Optional(string? value, int maximumLength) =>
        string.IsNullOrWhiteSpace(value) || value.Trim().Length <= maximumLength;

    private static bool IsOrderStateAllowed(
        ProductionOrderStatus status,
        MaterialTransactionType transactionType) => transactionType switch
        {
            MaterialTransactionType.OrderIssue => status is
                ProductionOrderStatus.Released
                or ProductionOrderStatus.InProduction
                or ProductionOrderStatus.Paused,
            MaterialTransactionType.OrderReturn or MaterialTransactionType.Reversal => status is
                ProductionOrderStatus.Released
                or ProductionOrderStatus.InProduction
                or ProductionOrderStatus.Paused
                or ProductionOrderStatus.ExecutionCompleted,
            _ => false,
        };

    private static string ActionFor(MaterialTransactionType? transactionType) => transactionType switch
    {
        MaterialTransactionType.LineSideTransfer => "MATERIAL_LINE_SIDE_TRANSFER",
        MaterialTransactionType.OrderIssue => "MATERIAL_ORDER_ISSUE",
        MaterialTransactionType.OrderReturn => "MATERIAL_ORDER_RETURN",
        MaterialTransactionType.Adjustment => "MATERIAL_ADJUSTMENT",
        _ => "MATERIAL_TRANSACTION_WRITE",
    };

    private static MaterialTransactionResult Result(
        MaterialTransaction transaction,
        bool isReplay) => new(
            transaction.Id,
            transaction.TransactionType.ToString(),
            "Recorded",
            isReplay,
            transaction.LineSideBalanceAfter,
            transaction.OrderAvailableBalanceAfter);

    private static string Hash<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, WebJson);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))
            .ToLowerInvariant();
    }

    private static string Normalize(string? value) => value!.Trim();

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeUnit(string? value) => Normalize(value).ToUpperInvariant();

    private static MaterialTransactionRejectedException InvalidRequest() => Rejected(
        "MATERIAL_TRANSACTION_INVALID",
        "物料事务字段不完整或数量无效；请检查契约版本、来源、幂等键、物料、Lot、单位、数量、时间及必填责任信息。",
        422);

    private static MaterialTransactionRejectedException Rejected(
        string code,
        string message,
        int statusCode) => new(code, message, statusCode);

    private sealed record ProposedTransaction(
        MaterialTransactionType TransactionType,
        string SourceSystem,
        string IdempotencyKey,
        string MaterialCode,
        string LotNumber,
        decimal Quantity,
        string Unit,
        Guid? ProductionOrderId,
        decimal LineSideDelta,
        decimal OrderAvailableDelta,
        decimal OrderIssuedDelta,
        string? SourceDocumentType,
        string? SourceDocumentNumber,
        string? FromParty,
        string? ToParty,
        string? Reason,
        DateTimeOffset OccurredAtUtc,
        string CommandHash,
        Guid? ReversesTransactionId = null);
}
