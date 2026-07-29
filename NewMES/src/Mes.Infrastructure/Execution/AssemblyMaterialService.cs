using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mes.Domain.Auditing;
using Mes.Domain.Execution;
using Mes.Domain.Identity;
using Mes.Domain.Materials;
using Mes.Domain.MasterData;
using Mes.Infrastructure.IdentityAccess;
using Mes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Mes.Infrastructure.Execution;

public sealed class AssemblyMaterialService(
    MesDbContext context,
    IdentityAccessService identityAccess,
    TimeProvider timeProvider)
{
    private const string ConsumeAction = "ASSEMBLY_MATERIAL_CONSUME";
    private const string HashAlgorithm = "SHA-256-JSON-V1";
    private const string ObjectType = "ProductIdentity";
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private static readonly HashSet<string> PreAssemblyOrLabelEventTypes =
    [
        "IDENTITY_ALLOCATED",
        "IDENTITY_BOUND",
        "START_WIP",
        "LABEL_PRINTED",
        "LABEL_REPRINTED",
        "LABEL_VOIDED",
        "LABEL_REPLACED",
    ];
    private static readonly HashSet<string> AssemblyOperationEventTypes =
    [
        "COMPONENT_BOUND",
        "COMPONENT_UNBOUND",
        "MATERIAL_CONSUMED",
        "MATERIAL_CONSUMPTION_REVERSED",
        "ASSEMBLY_OPERATION_COMPLETED",
        "ASSEMBLY_OPERATION_REOPENED",
    ];
    private readonly BusinessAuditWriter auditWriter = new(context, timeProvider);

    public async Task<AssemblyMaterialConsumeResult> ConsumeAsync(
        EffectiveIdentity actor,
        string finishedSerialNumber,
        AssemblyMaterialConsumeRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var serialNumber = Normalize(finishedSerialNumber);
        await identityAccess.DemandCapabilityAsync(
            actor,
            BusinessCapability.StationExecute,
            ConsumeAction,
            ObjectType,
            serialNumber,
            correlationId,
            cancellationToken);
        try
        {
            var command = Parse(request, serialNumber);
            var strategy = context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                context.ChangeTracker.Clear();
                await using var transaction = await context.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);
                var replay = await context.AssemblyCommandReceipts
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        item => item.SourceSystem == command.SourceSystem
                            && item.IdempotencyKey == command.IdempotencyKey,
                        cancellationToken);
                if (replay is not null)
                {
                    if (!string.Equals(replay.CommandHash, command.CommandHash, StringComparison.Ordinal))
                    {
                        throw Rejected(
                            "ASSEMBLY_IDEMPOTENCY_CONFLICT",
                            "相同来源和幂等键已用于另一条装配命令，请核对原请求。",
                            409);
                    }

                    var replayResult = JsonSerializer.Deserialize<AssemblyMaterialConsumeResult>(
                        replay.ResultJson,
                        WebJson) ?? throw Rejected(
                            "ASSEMBLY_RECEIPT_INVALID",
                            "装配命令回执无法读取，请停止操作并联系系统管理员。",
                            500);
                    await transaction.CommitAsync(cancellationToken);
                    return replayResult with { IsReplay = true };
                }

                var identity = await context.ProductIdentities
                    .Include(item => item.ProductionOrder)
                    .Include(item => item.ExecutionSnapshot)
                    .SingleOrDefaultAsync(item => item.SerialNumber == serialNumber, cancellationToken)
                    ?? throw Rejected(
                        "PRODUCT_IDENTITY_NOT_FOUND",
                        "未找到成品 SN，请核对扫描值并确认已经完成 START_WIP。",
                        404);
                if (identity.Status != ProductIdentityStatus.Bound
                    || identity.ProductionOrder is null
                    || identity.ExecutionSnapshot is null
                    || identity.ProductionOrderId is null
                    || identity.ExecutionSnapshotId is null)
                {
                    throw Rejected(
                        "ASSEMBLY_PRODUCT_NOT_IN_WIP",
                        "该成品尚未投入生产或已解除绑定，不能记录装配耗用。",
                        409);
                }

                var productionOrderId = identity.ProductionOrderId.Value;
                var executionSnapshotId = identity.ExecutionSnapshotId.Value;

                if (identity.ProductionOrder.Status != ProductionOrderStatus.InProduction)
                {
                    throw Rejected(
                        "ASSEMBLY_ORDER_STATUS_BLOCKED",
                        "生产订单当前不是执行中状态；暂停或终态订单不能继续装配。",
                        409);
                }

                var definition = DeserializeDefinition(identity.ExecutionSnapshot.DefinitionJson);
                var requirement = definition.Bom.Components.SingleOrDefault(item =>
                    string.Equals(item.MaterialCode, command.MaterialCode, StringComparison.Ordinal));
                if (requirement is null)
                {
                    throw Rejected(
                        "ASSEMBLY_MATERIAL_NOT_REQUIRED",
                        "该物料不属于当前订单快照在此工序的直接物料要求，请核对物料和工序。",
                        422);
                }

                var snapshotRequirement = ParseSnapshotRequirement(requirement);

                if (!string.Equals(identity.NextOperationCode, command.OperationCode, StringComparison.Ordinal)
                    || !string.Equals(
                        snapshotRequirement.OperationCode,
                        command.OperationCode,
                        StringComparison.Ordinal))
                {
                    throw Rejected(
                        "ASSEMBLY_OPERATION_MISMATCH",
                        $"成品当前应执行工序为 {identity.NextOperationCode ?? "<none>"}，不能在 {command.OperationCode} 记录耗用。",
                        409);
                }

                var material = await context.Materials.SingleOrDefaultAsync(
                    item => item.Code == command.MaterialCode,
                    cancellationToken) ?? throw Rejected(
                    "ASSEMBLY_MATERIAL_NOT_FOUND",
                    "订单快照引用的物料编码无法解析到物料标识，不能记录装配耗用。",
                    422);
                if (!string.Equals(requirement.Unit, command.Unit, StringComparison.Ordinal))
                {
                    throw Rejected(
                        "ASSEMBLY_MATERIAL_UNIT_MISMATCH",
                        "耗用单位必须匹配订单冻结 BOM 单位。",
                        422);
                }

                var traceabilityMode = snapshotRequirement.TraceabilityMode;

                var consumedQuantity = await ReadConsumedQuantityAsync(
                    identity.Id,
                    material.Id,
                    command.OperationCode,
                    cancellationToken);
                var remainingBeforeCommand = requirement.QuantityPer - consumedQuantity;
                var actualQuantity = ResolveActualQuantity(
                    traceabilityMode,
                    snapshotRequirement.ConsumptionRule,
                    command,
                    remainingBeforeCommand);
                if (traceabilityMode == TraceabilityMode.Serial
                    && (string.IsNullOrWhiteSpace(command.ComponentSerialNumber)
                        || actualQuantity != 1m))
                {
                    throw Rejected(
                        "ASSEMBLY_SERIAL_CAPTURE_INVALID",
                        "序列件必须逐个扫描部件 SN，并以 1 个基础单位记录。",
                        422);
                }

                if (traceabilityMode == TraceabilityMode.Serial
                    && await context.AssemblyComponentBindings.AnyAsync(
                        item => item.ComponentSerialNumber == command.ComponentSerialNumber
                            && item.IsActive,
                        cancellationToken))
                {
                    throw Rejected(
                        "COMPONENT_SERIAL_ALREADY_BOUND",
                        "该关键部件 SN 已存在有效成品关系，不能同时绑定到另一成品。",
                        409);
                }

                if (consumedQuantity + actualQuantity > requirement.QuantityPer)
                {
                    throw Rejected(
                        "ASSEMBLY_REQUIREMENT_EXCEEDED",
                        "本成品对此物料的实际耗用已达到订单快照要求，不能重复采集。",
                        409);
                }

                var transferred = await context.MaterialTransactions.AnyAsync(
                    item => item.MaterialId == material.Id
                        && item.LotNumber == command.LotNumber
                        && item.TransactionType == MaterialTransactionType.LineSideTransfer,
                    cancellationToken);
                if (!transferred)
                {
                    throw Rejected(
                        "ASSEMBLY_MATERIAL_NOT_TRANSFERRED",
                        "该物料 Lot 尚无线边交接事实，不能直接记录装配耗用。",
                        409);
                }

                var issued = await context.MaterialTransactions.AnyAsync(
                    item => item.MaterialId == material.Id
                        && item.LotNumber == command.LotNumber
                        && item.ProductionOrderId == productionOrderId
                        && item.TransactionType == MaterialTransactionType.OrderIssue,
                    cancellationToken);
                if (!issued)
                {
                    throw Rejected(
                        "ASSEMBLY_MATERIAL_NOT_ISSUED",
                        "该物料 Lot 尚未向当前生产订单发料；发料不等于耗用，但耗用必须有已发料余额。",
                        409);
                }

                var orderAvailable = await context.MaterialTransactions
                    .Where(item => item.MaterialId == material.Id
                        && item.LotNumber == command.LotNumber
                        && item.ProductionOrderId == productionOrderId)
                    .SumAsync(
                        item => (decimal?)item.OrderAvailableQuantityDelta,
                        cancellationToken) ?? 0m;
                if (orderAvailable < actualQuantity)
                {
                    throw Rejected(
                        "ASSEMBLY_MATERIAL_BALANCE_INSUFFICIENT",
                        $"当前订单的物料 {material.Code} / Lot {command.LotNumber} 可用量为 {orderAvailable} {command.Unit}，不足以耗用。",
                        409);
                }

                var now = timeProvider.GetUtcNow();
                var materialTransactionId = Guid.NewGuid();
                Guid? bindingId = null;
                ManufacturingEvent? bindingEvent = null;
                if (traceabilityMode == TraceabilityMode.Serial)
                {
                    bindingId = Guid.NewGuid();
                    bindingEvent = AppendEvent(
                        "COMPONENT_BOUND",
                        identity,
                        actor,
                        command.OccurredAtUtc,
                        now,
                        command.Location,
                        correlationId,
                        new
                        {
                            bindingId,
                            materialTransactionId,
                            materialCode = material.Code,
                            componentSerialNumber = command.ComponentSerialNumber,
                            lotNumber = command.LotNumber,
                            quantity = actualQuantity,
                            unit = command.Unit,
                            operationCode = command.OperationCode,
                        });
                }
                context.MaterialTransactions.Add(new MaterialTransaction
                {
                    Id = materialTransactionId,
                    TransactionType = MaterialTransactionType.Consumption,
                    MaterialId = material.Id,
                    ProductionOrderId = productionOrderId,
                    ProductIdentityId = identity.Id,
                    OperationCode = command.OperationCode,
                    TraceabilityMode = traceabilityMode,
                    LotNumber = command.LotNumber,
                    Quantity = actualQuantity,
                    Unit = command.Unit,
                    LineSideQuantityDelta = 0,
                    OrderAvailableQuantityDelta = -actualQuantity,
                    OrderIssuedQuantityDelta = 0,
                    SourceSystem = command.SourceSystem,
                    IdempotencyKey = command.IdempotencyKey,
                    SourceDocumentType = "ASSEMBLY_CONSUMPTION",
                    SourceDocumentNumber = identity.SerialNumber,
                    ActorUserId = actor.UserId,
                    OccurredAtUtc = command.OccurredAtUtc,
                    RecordedAtUtc = now,
                    CommandHash = command.CommandHash,
                    CommandHashAlgorithm = HashAlgorithm,
                    CorrelationId = correlationId,
                    LineSideBalanceAfter = await ReadLineSideBalanceAsync(
                        material.Id,
                        command.LotNumber,
                        cancellationToken),
                    OrderAvailableBalanceAfter = orderAvailable - actualQuantity,
                });
                if (bindingId is not null && bindingEvent is not null)
                {
                    context.AssemblyComponentBindings.Add(new AssemblyComponentBinding
                    {
                        Id = bindingId.Value,
                        ProductIdentityId = identity.Id,
                        ProductionOrderId = productionOrderId,
                        ExecutionSnapshotId = executionSnapshotId,
                        MaterialId = material.Id,
                        ComponentSerialNumber = command.ComponentSerialNumber,
                        LotNumber = command.LotNumber,
                        Quantity = actualQuantity,
                        Unit = command.Unit,
                        OperationCode = command.OperationCode,
                        ConsumptionTransactionId = materialTransactionId,
                        BindingEventId = bindingEvent.Id,
                        IsActive = true,
                        BoundAtUtc = now,
                        BoundByUserId = actor.UserId,
                    });
                }
                AppendEvent(
                    "MATERIAL_CONSUMED",
                    identity,
                    actor,
                    command.OccurredAtUtc,
                    now,
                    command.Location,
                    correlationId,
                    new
                    {
                        materialTransactionId,
                        materialCode = material.Code,
                        traceabilityMode = DisplayTraceabilityMode(traceabilityMode),
                        componentSerialNumber = command.ComponentSerialNumber,
                        lotNumber = command.LotNumber,
                        quantity = actualQuantity,
                        unit = command.Unit,
                        operationCode = command.OperationCode,
                    },
                    bindingEvent?.Id);

                var remainingQuantity = requirement.QuantityPer - consumedQuantity - actualQuantity;
                var operationCompleted = remainingQuantity == 0
                    && await OtherRequirementsAreCompleteAsync(
                        definition,
                        identity.Id,
                        command.OperationCode,
                        material.Id,
                        cancellationToken);
                if (operationCompleted)
                {
                    identity.NextOperationCode = NextOperationCode(definition, command.OperationCode);
                    AppendEvent(
                        "ASSEMBLY_OPERATION_COMPLETED",
                        identity,
                        actor,
                        command.OccurredAtUtc,
                        now,
                        command.Location,
                        correlationId,
                        new
                        {
                            operationCode = command.OperationCode,
                            nextOperationCode = identity.NextOperationCode,
                        },
                        bindingEvent?.Id);
                }

                var result = new AssemblyMaterialConsumeResult(
                    identity.Id,
                    identity.SerialNumber,
                    productionOrderId,
                    material.Code,
                    DisplayTraceabilityMode(traceabilityMode),
                    bindingId,
                    materialTransactionId,
                    actualQuantity,
                    remainingQuantity,
                    operationCompleted,
                    identity.NextOperationCode,
                    false);
                AppendAudit(
                    actor,
                    ConsumeAction,
                    identity.Id.ToString(),
                    BusinessAuditResult.Succeeded,
                    null,
                    correlationId);
                context.AssemblyCommandReceipts.Add(new AssemblyCommandReceipt
                {
                    Id = Guid.NewGuid(),
                    SourceSystem = command.SourceSystem,
                    IdempotencyKey = command.IdempotencyKey,
                    CommandType = "Consume",
                    CommandHash = command.CommandHash,
                    CommandHashAlgorithm = HashAlgorithm,
                    ProductIdentityId = identity.Id,
                    ResultJson = JsonSerializer.Serialize(result, WebJson),
                    CompletedAtUtc = now,
                });
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            });
        }
        catch (AssemblyMaterialRejectedException error)
        {
            await PersistDeniedAuditAsync(
                actor,
                ConsumeAction,
                serialNumber,
                error.Code,
                correlationId,
                cancellationToken);
            throw;
        }
    }

    public async Task<AssemblyMaterialRequirementsResult> ReadRequirementsAsync(
        EffectiveIdentity actor,
        string finishedSerialNumber,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var serialNumber = Normalize(finishedSerialNumber);
        await identityAccess.DemandCapabilityAsync(
            actor,
            BusinessCapability.StationExecute,
            "ASSEMBLY_MATERIAL_REQUIREMENTS_READ",
            ObjectType,
            serialNumber,
            correlationId,
            cancellationToken);
        var identity = await LoadBoundIdentityAsync(serialNumber, cancellationToken);
        var definition = DeserializeDefinition(identity.ExecutionSnapshot!.DefinitionJson);
        var materialCodes = definition.Bom.Components.Select(item => item.MaterialCode).Distinct().ToArray();
        var materials = await context.Materials
            .AsNoTracking()
            .Where(item => materialCodes.Contains(item.Code))
            .ToDictionaryAsync(item => item.Code, StringComparer.Ordinal, cancellationToken);
        var requirements = new List<AssemblyMaterialRequirementView>();
        foreach (var requirement in definition.Bom.Components.OrderBy(item => item.MaterialCode))
        {
            var snapshotRequirement = ParseSnapshotRequirement(requirement);
            var material = materials.GetValueOrDefault(requirement.MaterialCode)
                ?? throw Rejected(
                    "ASSEMBLY_SNAPSHOT_MATERIAL_MISSING",
                    "订单快照引用的物料主数据不存在，无法可靠显示装配要求。",
                    500);
            var consumed = await ReadConsumedQuantityAsync(
                identity.Id,
                material.Id,
                snapshotRequirement.OperationCode,
                cancellationToken);
            var bindings = await context.AssemblyComponentBindings
                .AsNoTracking()
                .Where(item => item.ProductIdentityId == identity.Id
                    && item.MaterialId == material.Id
                    && item.OperationCode == snapshotRequirement.OperationCode
                    && item.IsActive)
                .OrderBy(item => item.BoundAtUtc)
                .Select(item => new AssemblyBindingView(
                    item.Id,
                    item.ComponentSerialNumber,
                    item.LotNumber,
                    item.Quantity,
                    item.Unit,
                    "Active",
                    item.BoundAtUtc))
                .ToArrayAsync(cancellationToken);
            requirements.Add(new AssemblyMaterialRequirementView(
                material.Code,
                material.Name,
                snapshotRequirement.OperationCode,
                DisplayTraceabilityMode(snapshotRequirement.TraceabilityMode),
                snapshotRequirement.ConsumptionRule,
                requirement.QuantityPer,
                consumed,
                Math.Max(0, requirement.QuantityPer - consumed),
                requirement.Unit,
                bindings));
        }

        return new AssemblyMaterialRequirementsResult(
            identity.Id,
            identity.SerialNumber,
            identity.NextOperationCode,
            requirements);
    }

    public Task<AssemblyBindingCorrectionResult> UnbindAsync(
        EffectiveIdentity actor,
        Guid bindingId,
        AssemblyBindingUnbindRequest request,
        string correlationId,
        CancellationToken cancellationToken = default) => ExecuteCorrectionAsync(
            actor,
            bindingId,
            request.SourceSystem,
            request.IdempotencyKey,
            request.Reason,
            request.Location,
            request.OccurredAtUtc,
            null,
            null,
            "ASSEMBLY_COMPONENT_UNBIND",
            "Unbind",
            correlationId,
            cancellationToken);

    public Task<AssemblyBindingCorrectionResult> ReplaceAsync(
        EffectiveIdentity actor,
        Guid bindingId,
        AssemblyBindingReplaceRequest request,
        string correlationId,
        CancellationToken cancellationToken = default) => ExecuteCorrectionAsync(
            actor,
            bindingId,
            request.SourceSystem,
            request.IdempotencyKey,
            request.Reason,
            request.Location,
            request.OccurredAtUtc,
            request.NewComponentSerialNumber,
            request.LotNumber,
            "ASSEMBLY_COMPONENT_REPLACE",
            "Replace",
            correlationId,
            cancellationToken);

    public async Task<AssemblyConsumptionReverseResult> ReverseConsumptionAsync(
        EffectiveIdentity actor,
        Guid transactionId,
        AssemblyConsumptionReverseRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        const string action = "ASSEMBLY_CONSUMPTION_REVERSE";
        await identityAccess.DemandCapabilityAsync(
            actor,
            BusinessCapability.StationExecute,
            action,
            "MaterialTransaction",
            transactionId.ToString(),
            correlationId,
            cancellationToken);
        try
        {
            var sourceSystem = Normalize(request.SourceSystem);
            var idempotencyKey = Normalize(request.IdempotencyKey);
            var reason = Normalize(request.Reason);
            var location = Normalize(request.Location);
            if (!Required(sourceSystem, 80)
                || !Required(idempotencyKey, 100)
                || !Required(reason, 400)
                || !Required(location, 120)
                || request.OccurredAtUtc == default)
            {
                throw Rejected(
                    "ASSEMBLY_CONSUMPTION_REVERSAL_INVALID",
                    "耗用冲正必须包含来源、幂等键、原因、位置和发生时间。",
                    422);
            }

            var occurredAtUtc = request.OccurredAtUtc.ToUniversalTime();
            var commandHash = Hash(new
            {
                transactionId,
                sourceSystem,
                idempotencyKey,
                reason,
                location,
                occurredAtUtc,
            });
            var strategy = context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                context.ChangeTracker.Clear();
                await using var transaction = await context.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);
                var replay = await context.AssemblyCommandReceipts
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        item => item.SourceSystem == sourceSystem
                            && item.IdempotencyKey == idempotencyKey,
                        cancellationToken);
                if (replay is not null)
                {
                    if (!string.Equals(replay.CommandHash, commandHash, StringComparison.Ordinal)
                        || !string.Equals(replay.CommandType, "ReverseConsumption", StringComparison.Ordinal))
                    {
                        throw Rejected(
                            "ASSEMBLY_IDEMPOTENCY_CONFLICT",
                            "相同来源和幂等键已用于另一条装配命令，请核对原请求。",
                            409);
                    }

                    var replayResult = JsonSerializer.Deserialize<AssemblyConsumptionReverseResult>(
                        replay.ResultJson,
                        WebJson) ?? throw Rejected(
                            "ASSEMBLY_RECEIPT_INVALID",
                            "耗用冲正回执无法读取，请停止操作并联系系统管理员。",
                            500);
                    await transaction.CommitAsync(cancellationToken);
                    return replayResult with { IsReplay = true };
                }

                var original = await context.MaterialTransactions
                    .Include(item => item.ProductIdentity)
                    .SingleOrDefaultAsync(item => item.Id == transactionId, cancellationToken)
                    ?? throw Rejected(
                        "ASSEMBLY_CONSUMPTION_NOT_FOUND",
                        "未找到原耗用事务，请从成品物料谱系刷新后重试。",
                        404);
                if (original.TransactionType != MaterialTransactionType.Consumption
                    || original.ProductIdentity is null
                    || original.ProductIdentityId is null
                    || original.ProductionOrderId is null
                    || original.OperationCode is null
                    || original.TraceabilityMode is null)
                {
                    throw Rejected(
                        "ASSEMBLY_CONSUMPTION_NOT_REVERSIBLE",
                        "指定事务不是可冲正的成品装配耗用。",
                        409);
                }

                if (original.TraceabilityMode == TraceabilityMode.Serial)
                {
                    throw Rejected(
                        "ASSEMBLY_SERIAL_REQUIRES_UNBIND",
                        "序列件耗用必须通过部件解绑或替换同时维护有效关系。",
                        409);
                }

                if (await context.MaterialTransactions.AnyAsync(
                        item => item.ReversesTransactionId == original.Id,
                        cancellationToken))
                {
                    throw Rejected(
                        "ASSEMBLY_CONSUMPTION_ALREADY_REVERSED",
                        "该耗用已经存在冲正事务，不能再次冲正。",
                        409);
                }

                var identity = original.ProductIdentity;
                if (await HasDownstreamManufacturingFactsAsync(
                        identity.Id,
                        original.OperationCode,
                        cancellationToken))
                {
                    throw Rejected(
                        "ASSEMBLY_CORRECTION_DOWNSTREAM_EXISTS",
                        "该成品已产生后续制造事实，不能直接冲正耗用；请进入受控返工/质量流程。",
                        409);
                }

                var orderAvailable = await context.MaterialTransactions
                    .Where(item => item.ProductionOrderId == original.ProductionOrderId
                        && item.MaterialId == original.MaterialId
                        && item.LotNumber == original.LotNumber)
                    .SumAsync(item => (decimal?)item.OrderAvailableQuantityDelta, cancellationToken) ?? 0m;
                var now = timeProvider.GetUtcNow();
                var reversalTransactionId = Guid.NewGuid();
                context.MaterialTransactions.Add(new MaterialTransaction
                {
                    Id = reversalTransactionId,
                    TransactionType = MaterialTransactionType.Reversal,
                    MaterialId = original.MaterialId,
                    ProductionOrderId = original.ProductionOrderId,
                    ProductIdentityId = original.ProductIdentityId,
                    OperationCode = original.OperationCode,
                    TraceabilityMode = original.TraceabilityMode,
                    LotNumber = original.LotNumber,
                    Quantity = original.Quantity,
                    Unit = original.Unit,
                    LineSideQuantityDelta = -original.LineSideQuantityDelta,
                    OrderAvailableQuantityDelta = -original.OrderAvailableQuantityDelta,
                    OrderIssuedQuantityDelta = -original.OrderIssuedQuantityDelta,
                    SourceSystem = sourceSystem,
                    IdempotencyKey = $"{idempotencyKey}:REVERSAL",
                    SourceDocumentType = "ASSEMBLY_CONSUMPTION_REVERSAL",
                    SourceDocumentNumber = identity.SerialNumber,
                    ReversesTransactionId = original.Id,
                    Reason = reason,
                    ActorUserId = actor.UserId,
                    OccurredAtUtc = occurredAtUtc,
                    RecordedAtUtc = now,
                    CommandHash = commandHash,
                    CommandHashAlgorithm = HashAlgorithm,
                    CorrelationId = correlationId,
                    LineSideBalanceAfter = await ReadLineSideBalanceAsync(
                        original.MaterialId,
                        original.LotNumber,
                        cancellationToken),
                    OrderAvailableBalanceAfter = orderAvailable - original.OrderAvailableQuantityDelta,
                });
                var consumedEventId = await FindMaterialConsumedEventIdAsync(
                    identity.Id,
                    original.Id,
                    cancellationToken);
                var reversalEvent = AppendEvent(
                    "MATERIAL_CONSUMPTION_REVERSED",
                    identity,
                    actor,
                    occurredAtUtc,
                    now,
                    location,
                    correlationId,
                    new
                    {
                        operationCode = original.OperationCode,
                        originalTransactionId = original.Id,
                        reversalTransactionId,
                        reason,
                    },
                    correctsEventId: consumedEventId);
                await ReopenAssemblyOperationIfCompletedAsync(
                    identity,
                    original.OperationCode,
                    actor,
                    occurredAtUtc,
                    now,
                    location,
                    correlationId,
                    reversalEvent.Id,
                    cancellationToken);
                var result = new AssemblyConsumptionReverseResult(
                    identity.Id,
                    identity.SerialNumber,
                    original.Id,
                    reversalTransactionId,
                    "Reversed",
                    identity.NextOperationCode,
                    false);
                AppendAudit(
                    actor,
                    action,
                    original.Id.ToString(),
                    BusinessAuditResult.Succeeded,
                    null,
                    correlationId);
                context.AssemblyCommandReceipts.Add(new AssemblyCommandReceipt
                {
                    Id = Guid.NewGuid(),
                    SourceSystem = sourceSystem,
                    IdempotencyKey = idempotencyKey,
                    CommandType = "ReverseConsumption",
                    CommandHash = commandHash,
                    CommandHashAlgorithm = HashAlgorithm,
                    ProductIdentityId = identity.Id,
                    ResultJson = JsonSerializer.Serialize(result, WebJson),
                    CompletedAtUtc = now,
                });
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            });
        }
        catch (AssemblyMaterialRejectedException error)
        {
            await PersistDeniedAuditAsync(
                actor,
                action,
                transactionId.ToString(),
                error.Code,
                correlationId,
                cancellationToken);
            throw;
        }
    }

    private async Task<AssemblyBindingCorrectionResult> ExecuteCorrectionAsync(
        EffectiveIdentity actor,
        Guid bindingId,
        string? requestedSourceSystem,
        string? requestedIdempotencyKey,
        string? requestedReason,
        string? requestedLocation,
        DateTimeOffset occurredAtUtc,
        string? requestedNewComponentSerialNumber,
        string? requestedLotNumber,
        string action,
        string commandType,
        string correlationId,
        CancellationToken cancellationToken)
    {
        await identityAccess.DemandCapabilityAsync(
            actor,
            BusinessCapability.StationExecute,
            action,
            "AssemblyComponentBinding",
            bindingId.ToString(),
            correlationId,
            cancellationToken);
        try
        {
            var sourceSystem = Normalize(requestedSourceSystem);
            var idempotencyKey = Normalize(requestedIdempotencyKey);
            var reason = Normalize(requestedReason);
            var location = Normalize(requestedLocation);
            var newComponentSerialNumber = Normalize(requestedNewComponentSerialNumber);
            var lotNumber = Normalize(requestedLotNumber);
            var isReplacement = string.Equals(commandType, "Replace", StringComparison.Ordinal);
            if (!Required(sourceSystem, 80)
                || !Required(idempotencyKey, 100)
                || !Required(reason, 400)
                || !Required(location, 120)
                || occurredAtUtc == default
                || (isReplacement
                    && (!Required(newComponentSerialNumber, 200) || !Required(lotNumber, 120))))
            {
                throw Rejected(
                    "ASSEMBLY_CORRECTION_INVALID",
                    "解绑或替换必须包含来源、幂等键、原因、位置、发生时间；替换还必须包含新部件 SN 和 Lot。",
                    422);
            }

            occurredAtUtc = occurredAtUtc.ToUniversalTime();
            var commandHash = Hash(new
            {
                bindingId,
                sourceSystem,
                idempotencyKey,
                reason,
                location,
                occurredAtUtc,
                newComponentSerialNumber = isReplacement ? newComponentSerialNumber : null,
                lotNumber = isReplacement ? lotNumber : null,
                commandType,
            });
            var strategy = context.Database.CreateExecutionStrategy();
            return await strategy.ExecuteAsync(async () =>
            {
                context.ChangeTracker.Clear();
                await using var transaction = await context.Database.BeginTransactionAsync(
                    IsolationLevel.Serializable,
                    cancellationToken);
                var replay = await context.AssemblyCommandReceipts
                    .AsNoTracking()
                    .SingleOrDefaultAsync(
                        item => item.SourceSystem == sourceSystem
                            && item.IdempotencyKey == idempotencyKey,
                        cancellationToken);
                if (replay is not null)
                {
                    if (!string.Equals(replay.CommandHash, commandHash, StringComparison.Ordinal)
                        || !string.Equals(replay.CommandType, commandType, StringComparison.Ordinal))
                    {
                        throw Rejected(
                            "ASSEMBLY_IDEMPOTENCY_CONFLICT",
                            "相同来源和幂等键已用于另一条装配命令，请核对原请求。",
                            409);
                    }

                    var replayResult = JsonSerializer.Deserialize<AssemblyBindingCorrectionResult>(
                        replay.ResultJson,
                        WebJson) ?? throw Rejected(
                            "ASSEMBLY_RECEIPT_INVALID",
                            "装配纠错回执无法读取，请停止操作并联系系统管理员。",
                            500);
                    await transaction.CommitAsync(cancellationToken);
                    return replayResult with { IsReplay = true };
                }

                var binding = await context.AssemblyComponentBindings
                    .Include(item => item.ProductIdentity)
                    .Include(item => item.Material)
                    .Include(item => item.ConsumptionTransaction)
                    .SingleOrDefaultAsync(item => item.Id == bindingId, cancellationToken)
                    ?? throw Rejected(
                        "ASSEMBLY_BINDING_NOT_FOUND",
                        "未找到部件绑定关系，请刷新成品谱系后重试。",
                        404);
                if (!binding.IsActive
                    || binding.ProductIdentity is null
                    || binding.Material is null
                    || binding.ConsumptionTransaction is null)
                {
                    throw Rejected(
                        "ASSEMBLY_BINDING_NOT_ACTIVE",
                        "该部件关系已经解绑或被替换，不能再次纠错。",
                        409);
                }

                var identity = binding.ProductIdentity;
                if (await HasDownstreamManufacturingFactsAsync(
                        identity.Id,
                        binding.OperationCode,
                        cancellationToken))
                {
                    throw Rejected(
                        "ASSEMBLY_CORRECTION_DOWNSTREAM_EXISTS",
                        "该成品已产生后续制造事实，不能直接解绑或替换；请进入受控返工/质量流程。",
                        409);
                }

                if (isReplacement
                    && (string.Equals(
                            binding.ComponentSerialNumber,
                            newComponentSerialNumber,
                            StringComparison.Ordinal)
                        || await context.AssemblyComponentBindings.AnyAsync(
                            item => item.ComponentSerialNumber == newComponentSerialNumber
                                && item.IsActive,
                            cancellationToken)))
                {
                    throw Rejected(
                        "COMPONENT_SERIAL_ALREADY_BOUND",
                        "替换用关键部件 SN 已存在有效关系或与原部件相同。",
                        409);
                }

                var original = binding.ConsumptionTransaction;
                var originalLotAvailable = await context.MaterialTransactions
                    .Where(item => item.ProductionOrderId == binding.ProductionOrderId
                        && item.MaterialId == binding.MaterialId
                        && item.LotNumber == original.LotNumber)
                    .SumAsync(item => (decimal?)item.OrderAvailableQuantityDelta, cancellationToken) ?? 0m;
                decimal replacementLotAvailable = 0m;
                if (isReplacement)
                {
                    var transferred = await context.MaterialTransactions.AnyAsync(
                        item => item.MaterialId == binding.MaterialId
                            && item.LotNumber == lotNumber
                            && item.TransactionType == MaterialTransactionType.LineSideTransfer,
                        cancellationToken);
                    var issued = await context.MaterialTransactions.AnyAsync(
                        item => item.MaterialId == binding.MaterialId
                            && item.LotNumber == lotNumber
                            && item.ProductionOrderId == binding.ProductionOrderId
                            && item.TransactionType == MaterialTransactionType.OrderIssue,
                        cancellationToken);
                    if (!transferred || !issued)
                    {
                        throw Rejected(
                            transferred
                                ? "ASSEMBLY_MATERIAL_NOT_ISSUED"
                                : "ASSEMBLY_MATERIAL_NOT_TRANSFERRED",
                            "替换部件对应 Lot 必须已经完成线边交接并向当前订单发料。",
                            409);
                    }

                    replacementLotAvailable = string.Equals(
                        lotNumber,
                        original.LotNumber,
                        StringComparison.Ordinal)
                        ? originalLotAvailable + original.Quantity
                        : await context.MaterialTransactions
                            .Where(item => item.ProductionOrderId == binding.ProductionOrderId
                                && item.MaterialId == binding.MaterialId
                                && item.LotNumber == lotNumber)
                            .SumAsync(
                                item => (decimal?)item.OrderAvailableQuantityDelta,
                                cancellationToken) ?? 0m;
                    if (replacementLotAvailable < original.Quantity)
                    {
                        throw Rejected(
                            "ASSEMBLY_MATERIAL_BALANCE_INSUFFICIENT",
                            "替换部件对应 Lot 的订单可用量不足，不能形成部分纠错事实。",
                            409);
                    }
                }

                var now = timeProvider.GetUtcNow();
                var reversalTransactionId = Guid.NewGuid();
                var lineSideBalance = await ReadLineSideBalanceAsync(
                    binding.MaterialId,
                    original.LotNumber,
                    cancellationToken);
                context.MaterialTransactions.Add(new MaterialTransaction
                {
                    Id = reversalTransactionId,
                    TransactionType = MaterialTransactionType.Reversal,
                    MaterialId = binding.MaterialId,
                    ProductionOrderId = binding.ProductionOrderId,
                    ProductIdentityId = binding.ProductIdentityId,
                    OperationCode = binding.OperationCode,
                    TraceabilityMode = TraceabilityMode.Serial,
                    LotNumber = original.LotNumber,
                    Quantity = original.Quantity,
                    Unit = original.Unit,
                    LineSideQuantityDelta = 0,
                    OrderAvailableQuantityDelta = original.Quantity,
                    OrderIssuedQuantityDelta = 0,
                    SourceSystem = sourceSystem,
                    IdempotencyKey = $"{idempotencyKey}:REVERSAL",
                    SourceDocumentType = "ASSEMBLY_CONSUMPTION_REVERSAL",
                    SourceDocumentNumber = identity.SerialNumber,
                    ReversesTransactionId = original.Id,
                    Reason = reason,
                    ActorUserId = actor.UserId,
                    OccurredAtUtc = occurredAtUtc,
                    RecordedAtUtc = now,
                    CommandHash = Hash(new { commandHash, phase = "REVERSAL" }),
                    CommandHashAlgorithm = HashAlgorithm,
                    CorrelationId = correlationId,
                    LineSideBalanceAfter = lineSideBalance,
                    OrderAvailableBalanceAfter = originalLotAvailable + original.Quantity,
                });
                binding.IsActive = false;
                binding.UnboundAtUtc = now;
                binding.UnboundByUserId = actor.UserId;
                binding.CorrectionReason = reason;
                var unboundEvent = AppendEvent(
                    "COMPONENT_UNBOUND",
                    identity,
                    actor,
                    occurredAtUtc,
                    now,
                    location,
                    correlationId,
                    new
                    {
                        bindingId = binding.Id,
                        componentSerialNumber = binding.ComponentSerialNumber,
                        operationCode = binding.OperationCode,
                        reason,
                        reversalTransactionId,
                    },
                    correctsEventId: binding.BindingEventId);
                var consumedEventId = await context.ManufacturingEvents
                    .Where(item => item.EventType == "MATERIAL_CONSUMED"
                        && item.CausationEventId == binding.BindingEventId)
                    .Select(item => (Guid?)item.Id)
                    .SingleOrDefaultAsync(cancellationToken);
                var reversalEvent = AppendEvent(
                    "MATERIAL_CONSUMPTION_REVERSED",
                    identity,
                    actor,
                    occurredAtUtc,
                    now,
                    location,
                    correlationId,
                    new
                    {
                        operationCode = binding.OperationCode,
                        originalTransactionId = original.Id,
                        reversalTransactionId,
                        reason,
                    },
                    unboundEvent.Id,
                    consumedEventId ?? binding.BindingEventId);

                Guid? replacementBindingId = null;
                Guid? replacementTransactionId = null;
                if (isReplacement)
                {
                    replacementBindingId = Guid.NewGuid();
                    replacementTransactionId = Guid.NewGuid();
                    var replacementBindingEvent = AppendEvent(
                        "COMPONENT_BOUND",
                        identity,
                        actor,
                        occurredAtUtc,
                        now,
                        location,
                        correlationId,
                        new
                        {
                            bindingId = replacementBindingId,
                            materialTransactionId = replacementTransactionId,
                            materialCode = binding.Material.Code,
                            componentSerialNumber = newComponentSerialNumber,
                            lotNumber,
                            quantity = original.Quantity,
                            unit = original.Unit,
                            operationCode = binding.OperationCode,
                            replacesBindingId = binding.Id,
                        },
                        unboundEvent.Id,
                        binding.BindingEventId);
                    context.MaterialTransactions.Add(new MaterialTransaction
                    {
                        Id = replacementTransactionId.Value,
                        TransactionType = MaterialTransactionType.Consumption,
                        MaterialId = binding.MaterialId,
                        ProductionOrderId = binding.ProductionOrderId,
                        ProductIdentityId = binding.ProductIdentityId,
                        OperationCode = binding.OperationCode,
                        TraceabilityMode = TraceabilityMode.Serial,
                        LotNumber = lotNumber,
                        Quantity = original.Quantity,
                        Unit = original.Unit,
                        LineSideQuantityDelta = 0,
                        OrderAvailableQuantityDelta = -original.Quantity,
                        OrderIssuedQuantityDelta = 0,
                        SourceSystem = sourceSystem,
                        IdempotencyKey = $"{idempotencyKey}:REPLACEMENT",
                        SourceDocumentType = "ASSEMBLY_REPLACEMENT_CONSUMPTION",
                        SourceDocumentNumber = identity.SerialNumber,
                        ActorUserId = actor.UserId,
                        OccurredAtUtc = occurredAtUtc,
                        RecordedAtUtc = now,
                        CommandHash = Hash(new { commandHash, phase = "REPLACEMENT" }),
                        CommandHashAlgorithm = HashAlgorithm,
                        CorrelationId = correlationId,
                        LineSideBalanceAfter = await ReadLineSideBalanceAsync(
                            binding.MaterialId,
                            lotNumber,
                            cancellationToken),
                        OrderAvailableBalanceAfter = replacementLotAvailable - original.Quantity,
                    });
                    context.AssemblyComponentBindings.Add(new AssemblyComponentBinding
                    {
                        Id = replacementBindingId.Value,
                        ProductIdentityId = binding.ProductIdentityId,
                        ProductionOrderId = binding.ProductionOrderId,
                        ExecutionSnapshotId = binding.ExecutionSnapshotId,
                        MaterialId = binding.MaterialId,
                        ComponentSerialNumber = newComponentSerialNumber,
                        LotNumber = lotNumber,
                        Quantity = original.Quantity,
                        Unit = original.Unit,
                        OperationCode = binding.OperationCode,
                        ConsumptionTransactionId = replacementTransactionId.Value,
                        BindingEventId = replacementBindingEvent.Id,
                        IsActive = true,
                        BoundAtUtc = now,
                        BoundByUserId = actor.UserId,
                    });
                    AppendEvent(
                        "MATERIAL_CONSUMED",
                        identity,
                        actor,
                        occurredAtUtc,
                        now,
                        location,
                        correlationId,
                        new
                        {
                            materialTransactionId = replacementTransactionId,
                            materialCode = binding.Material.Code,
                            traceabilityMode = "Serial",
                            componentSerialNumber = newComponentSerialNumber,
                            lotNumber,
                            quantity = original.Quantity,
                            unit = original.Unit,
                            operationCode = binding.OperationCode,
                            replacesTransactionId = original.Id,
                        },
                        replacementBindingEvent.Id);
                }
                else
                {
                    await ReopenAssemblyOperationIfCompletedAsync(
                        identity,
                        binding.OperationCode,
                        actor,
                        occurredAtUtc,
                        now,
                        location,
                        correlationId,
                        reversalEvent.Id,
                        cancellationToken);
                }

                var result = new AssemblyBindingCorrectionResult(
                    identity.Id,
                    identity.SerialNumber,
                    binding.Id,
                    original.Id,
                    reversalTransactionId,
                    replacementBindingId,
                    replacementTransactionId,
                    isReplacement ? "Replaced" : "Unbound",
                    identity.NextOperationCode,
                    false);
                AppendAudit(
                    actor,
                    action,
                    binding.Id.ToString(),
                    BusinessAuditResult.Succeeded,
                    null,
                    correlationId);
                context.AssemblyCommandReceipts.Add(new AssemblyCommandReceipt
                {
                    Id = Guid.NewGuid(),
                    SourceSystem = sourceSystem,
                    IdempotencyKey = idempotencyKey,
                    CommandType = commandType,
                    CommandHash = commandHash,
                    CommandHashAlgorithm = HashAlgorithm,
                    ProductIdentityId = identity.Id,
                    ResultJson = JsonSerializer.Serialize(result, WebJson),
                    CompletedAtUtc = now,
                });
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return result;
            });
        }
        catch (AssemblyMaterialRejectedException error)
        {
            await PersistDeniedAuditAsync(
                actor,
                action,
                bindingId.ToString(),
                error.Code,
                correlationId,
                cancellationToken);
            throw;
        }
    }

    public async Task<ProductMaterialGenealogyResult> ReadProductGenealogyAsync(
        EffectiveIdentity actor,
        string finishedSerialNumber,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var serialNumber = Normalize(finishedSerialNumber);
        await identityAccess.DemandCapabilityAsync(
            actor,
            BusinessCapability.GenealogyRead,
            "PRODUCT_MATERIAL_GENEALOGY_READ",
            ObjectType,
            serialNumber,
            correlationId,
            cancellationToken);
        var identity = await LoadBoundIdentityAsync(serialNumber, cancellationToken);
        var transactions = await context.MaterialTransactions
            .AsNoTracking()
            .Include(item => item.Material)
            .Where(item => item.ProductIdentityId == identity.Id
                && (item.TransactionType == MaterialTransactionType.Consumption
                    || item.TransactionType == MaterialTransactionType.Reversal))
            .OrderBy(item => item.RecordedAtUtc)
            .ToArrayAsync(cancellationToken);
        var transactionIds = transactions
            .Select(item => item.ReversesTransactionId ?? item.Id)
            .Distinct()
            .ToArray();
        var bindings = await context.AssemblyComponentBindings
            .AsNoTracking()
            .Where(item => transactionIds.Contains(item.ConsumptionTransactionId))
            .ToDictionaryAsync(item => item.ConsumptionTransactionId, cancellationToken);
        var views = transactions.Select(item =>
        {
            bindings.TryGetValue(item.ReversesTransactionId ?? item.Id, out var binding);
            var netConsumedQuantity = item.TransactionType == MaterialTransactionType.Reversal
                ? -item.Quantity
                : item.Quantity;
            return new ProductMaterialConsumptionView(
                item.Id,
                item.TransactionType.ToString(),
                item.Material!.Code,
                DisplayTraceabilityMode(item.TraceabilityMode!.Value),
                item.OperationCode!,
                binding?.ComponentSerialNumber,
                item.LotNumber,
                item.Quantity,
                item.Unit,
                binding?.Id,
                binding is null ? null : binding.IsActive ? "Active" : "Unbound",
                item.ReversesTransactionId,
                item.Reason,
                netConsumedQuantity,
                item.OccurredAtUtc);
        }).ToArray();
        return new ProductMaterialGenealogyResult(
            identity.Id,
            identity.SerialNumber,
            identity.ProductionOrderId!.Value,
            views);
    }

    public async Task<LotImpactResult> ReadLotImpactAsync(
        EffectiveIdentity actor,
        string materialCode,
        string lotNumber,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var normalizedMaterialCode = Normalize(materialCode);
        var normalizedLotNumber = Normalize(lotNumber);
        await identityAccess.DemandCapabilityAsync(
            actor,
            BusinessCapability.GenealogyRead,
            "LOT_PRODUCT_IMPACT_READ",
            "MaterialLot",
            $"{normalizedMaterialCode}:{normalizedLotNumber}",
            correlationId,
            cancellationToken);
        if (!Required(normalizedMaterialCode, 80) || !Required(normalizedLotNumber, 120))
        {
            throw Rejected(
                "LOT_IMPACT_QUERY_INVALID",
                "按 Lot 反查时必须提供有效物料编码和 Lot。",
                422);
        }

        var material = await context.Materials
            .AsNoTracking()
            .SingleOrDefaultAsync(item => item.Code == normalizedMaterialCode, cancellationToken)
            ?? throw Rejected(
                "ASSEMBLY_MATERIAL_NOT_FOUND",
                "物料不存在，无法执行 Lot 影响查询。",
                404);
        var transactions = await context.MaterialTransactions
            .AsNoTracking()
            .Where(item => item.MaterialId == material.Id
                && item.LotNumber == normalizedLotNumber
                && item.ProductIdentityId != null
                && (item.TransactionType == MaterialTransactionType.Consumption
                    || item.TransactionType == MaterialTransactionType.Reversal))
            .Select(item => new
            {
                ProductIdentityId = item.ProductIdentityId!.Value,
                item.OrderAvailableQuantityDelta,
                item.Unit,
            })
            .ToArrayAsync(cancellationToken);
        var impacts = transactions
            .GroupBy(item => item.ProductIdentityId)
            .Select(group => new
            {
                ProductIdentityId = group.Key,
                NetQuantity = -group.Sum(item => item.OrderAvailableQuantityDelta),
                Unit = group.First().Unit,
            })
            .Where(item => item.NetQuantity > 0)
            .ToArray();
        var identityIds = impacts.Select(item => item.ProductIdentityId).ToArray();
        var identities = await context.ProductIdentities
            .AsNoTracking()
            .Include(item => item.ProductionOrder)
            .Where(item => identityIds.Contains(item.Id))
            .ToDictionaryAsync(item => item.Id, cancellationToken);
        var affectedProducts = impacts.Select(item =>
        {
            var identity = identities[item.ProductIdentityId];
            return new LotAffectedProductView(
                identity.Id,
                identity.SerialNumber,
                identity.ProductionOrderId!.Value,
                identity.ProductionOrder!.OrderNumber,
                item.NetQuantity,
                item.Unit);
        }).OrderBy(item => item.FinishedSerialNumber, StringComparer.Ordinal).ToArray();
        return new LotImpactResult(
            material.Code,
            normalizedLotNumber,
            affectedProducts);
    }

    public async Task<ComponentImpactResult> ReadComponentImpactAsync(
        EffectiveIdentity actor,
        string componentSerialNumber,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var normalizedSerialNumber = Normalize(componentSerialNumber);
        await identityAccess.DemandCapabilityAsync(
            actor,
            BusinessCapability.GenealogyRead,
            "COMPONENT_PRODUCT_IMPACT_READ",
            "ComponentSerial",
            normalizedSerialNumber,
            correlationId,
            cancellationToken);
        if (!Required(normalizedSerialNumber, 200))
        {
            throw Rejected(
                "COMPONENT_IMPACT_QUERY_INVALID",
                "按关键件反查时必须提供有效部件 SN。",
                422);
        }

        var bindings = await context.AssemblyComponentBindings
            .AsNoTracking()
            .Include(item => item.ProductIdentity)
                .ThenInclude(item => item!.ProductionOrder)
            .Include(item => item.Material)
            .Where(item => item.ComponentSerialNumber == normalizedSerialNumber)
            .OrderBy(item => item.BoundAtUtc)
            .ToArrayAsync(cancellationToken);
        var relationships = bindings.Select(item => new ComponentRelationshipImpactView(
            item.Id,
            item.ProductIdentityId,
            item.ProductIdentity!.SerialNumber,
            item.ProductionOrderId,
            item.ProductIdentity.ProductionOrder!.OrderNumber,
            item.Material!.Code,
            item.LotNumber,
            item.IsActive ? "Active" : "Unbound",
            item.BoundAtUtc,
            item.UnboundAtUtc,
            item.CorrectionReason)).ToArray();
        return new ComponentImpactResult(normalizedSerialNumber, relationships);
    }

    private async Task<ProductIdentity> LoadBoundIdentityAsync(
        string serialNumber,
        CancellationToken cancellationToken) =>
        await context.ProductIdentities
            .AsNoTracking()
            .Include(item => item.ExecutionSnapshot)
            .SingleOrDefaultAsync(item => item.SerialNumber == serialNumber, cancellationToken)
        is { Status: ProductIdentityStatus.Bound, ExecutionSnapshot: not null } identity
            ? identity
            : throw Rejected(
                "ASSEMBLY_PRODUCT_NOT_IN_WIP",
                "未找到已投产的成品 SN，无法读取装配物料。",
                404);

    private async Task<bool> HasDownstreamManufacturingFactsAsync(
        Guid productIdentityId,
        string assemblyOperationCode,
        CancellationToken cancellationToken)
    {
        var definitionJson = await context.ProductIdentities
            .AsNoTracking()
            .Where(item => item.Id == productIdentityId)
            .Select(item => item.ExecutionSnapshot!.DefinitionJson)
            .SingleAsync(cancellationToken);
        var operationSequences = DeserializeDefinition(definitionJson)
            .Route
            .Operations
            .ToDictionary(item => item.Code, item => item.Sequence, StringComparer.Ordinal);
        if (!operationSequences.TryGetValue(assemblyOperationCode, out var assemblySequence))
        {
            throw Rejected(
                "ASSEMBLY_SNAPSHOT_OPERATION_MISSING",
                "订单执行快照不包含待纠错的装配工序，不能可靠判断后续制造事实。",
                500);
        }

        var events = await context.ManufacturingEvents
            .AsNoTracking()
            .Where(item => item.ProductIdentityId == productIdentityId)
            .Select(item => new { item.EventType, item.PayloadJson })
            .ToArrayAsync(cancellationToken);
        return events.Any(item =>
        {
            if (PreAssemblyOrLabelEventTypes.Contains(item.EventType))
            {
                return false;
            }

            if (!AssemblyOperationEventTypes.Contains(item.EventType))
            {
                return true;
            }

            var eventOperationCode = ReadPayloadText(item.PayloadJson, "operationCode");
            return string.IsNullOrWhiteSpace(eventOperationCode)
                || !operationSequences.TryGetValue(eventOperationCode, out var eventSequence)
                || eventSequence > assemblySequence;
        });
    }

    private async Task ReopenAssemblyOperationIfCompletedAsync(
        ProductIdentity identity,
        string operationCode,
        EffectiveIdentity actor,
        DateTimeOffset occurredAtUtc,
        DateTimeOffset recordedAtUtc,
        string location,
        string correlationId,
        Guid causationEventId,
        CancellationToken cancellationToken)
    {
        if (string.Equals(identity.NextOperationCode, operationCode, StringComparison.Ordinal))
        {
            return;
        }

        var completionEvents = await context.ManufacturingEvents
            .AsNoTracking()
            .Where(item => item.ProductIdentityId == identity.Id
                && item.EventType == "ASSEMBLY_OPERATION_COMPLETED")
            .OrderByDescending(item => item.RecordedAtUtc)
            .ToArrayAsync(cancellationToken);
        var completionEvent = completionEvents.FirstOrDefault(item => string.Equals(
            ReadPayloadText(item.PayloadJson, "operationCode"),
            operationCode,
            StringComparison.Ordinal)) ?? throw Rejected(
                "ASSEMBLY_COMPLETION_EVENT_MISSING",
                "当前工序状态缺少对应的完成事件，不能通过纠错静默回退。",
                500);
        identity.NextOperationCode = operationCode;
        AppendEvent(
            "ASSEMBLY_OPERATION_REOPENED",
            identity,
            actor,
            occurredAtUtc,
            recordedAtUtc,
            location,
            correlationId,
            new
            {
                operationCode,
                reason = "Material correction reopened the completed assembly operation.",
            },
            causationEventId,
            completionEvent.Id);
    }

    private async Task<Guid?> FindMaterialConsumedEventIdAsync(
        Guid productIdentityId,
        Guid materialTransactionId,
        CancellationToken cancellationToken)
    {
        var events = await context.ManufacturingEvents
            .AsNoTracking()
            .Where(item => item.ProductIdentityId == productIdentityId
                && item.EventType == "MATERIAL_CONSUMED")
            .Select(item => new { item.Id, item.PayloadJson })
            .ToArrayAsync(cancellationToken);
        return events
            .Where(item => Guid.TryParse(
                ReadPayloadText(item.PayloadJson, "materialTransactionId"),
                out var eventTransactionId)
                && eventTransactionId == materialTransactionId)
            .Select(item => (Guid?)item.Id)
            .SingleOrDefault();
    }

    private static string? ReadPayloadText(string payloadJson, string propertyName)
    {
        using var document = JsonDocument.Parse(payloadJson);
        return document.RootElement.TryGetProperty(propertyName, out var property)
            ? property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : property.GetRawText().Trim('"')
            : null;
    }

    private async Task<decimal> ReadConsumedQuantityAsync(
        Guid productIdentityId,
        Guid materialId,
        string operationCode,
        CancellationToken cancellationToken) =>
        -(await context.MaterialTransactions
            .Where(item => item.ProductIdentityId == productIdentityId
                && item.MaterialId == materialId
                && item.OperationCode == operationCode)
            .SumAsync(item => (decimal?)item.OrderAvailableQuantityDelta, cancellationToken) ?? 0m);

    private async Task<decimal> ReadLineSideBalanceAsync(
        Guid materialId,
        string lotNumber,
        CancellationToken cancellationToken) =>
        await context.MaterialTransactions
            .Where(item => item.MaterialId == materialId && item.LotNumber == lotNumber)
            .SumAsync(item => (decimal?)item.LineSideQuantityDelta, cancellationToken) ?? 0m;

    private async Task<bool> OtherRequirementsAreCompleteAsync(
        ExecutionTemplateDefinition definition,
        Guid productIdentityId,
        string operationCode,
        Guid currentMaterialId,
        CancellationToken cancellationToken)
    {
        var otherRequirements = definition.Bom.Components
            .Where(item => string.Equals(
                    item.AssemblyOperationCode,
                    operationCode,
                    StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(item.MaterialCode))
            .ToArray();
        foreach (var requirement in otherRequirements)
        {
            var materialId = await context.Materials
                .Where(item => item.Code == requirement.MaterialCode)
                .Select(item => item.Id)
                .SingleAsync(cancellationToken);
            if (materialId == currentMaterialId)
            {
                continue;
            }

            var consumed = await ReadConsumedQuantityAsync(
                productIdentityId,
                materialId,
                operationCode,
                cancellationToken);
            if (consumed < requirement.QuantityPer)
            {
                return false;
            }
        }

        return true;
    }

    private ManufacturingEvent AppendEvent(
        string eventType,
        ProductIdentity identity,
        EffectiveIdentity actor,
        DateTimeOffset occurredAtUtc,
        DateTimeOffset recordedAtUtc,
        string location,
        string correlationId,
        object payload,
        Guid? causationEventId = null,
        Guid? correctsEventId = null)
    {
        var manufacturingEvent = new ManufacturingEvent
        {
            Id = Guid.NewGuid(),
            EventType = eventType,
            AggregateType = ObjectType,
            AggregateId = identity.Id.ToString(),
            OccurredAtUtc = occurredAtUtc,
            RecordedAtUtc = recordedAtUtc,
            Actor = actor.Username,
            PayloadJson = JsonSerializer.Serialize(payload, WebJson),
            ProductIdentityId = identity.Id,
            ProductionOrderId = identity.ProductionOrderId,
            ExecutionSnapshotId = identity.ExecutionSnapshotId,
            Location = location,
            CorrelationId = correlationId,
            CausationEventId = causationEventId,
            CorrectsEventId = correctsEventId,
        };
        context.ManufacturingEvents.Add(manufacturingEvent);
        return manufacturingEvent;
    }

    private void AppendAudit(
        EffectiveIdentity actor,
        string action,
        string objectId,
        BusinessAuditResult result,
        string? reasonCode,
        string correlationId) => auditWriter.Append(new BusinessAuditWrite(
            BusinessAuditActor.From(actor),
            BusinessRole.Operator,
            BusinessCapability.StationExecute,
            action,
            ObjectType,
            objectId,
            result,
            reasonCode,
            correlationId));

    private async Task PersistDeniedAuditAsync(
        EffectiveIdentity actor,
        string action,
        string objectId,
        string reasonCode,
        string correlationId,
        CancellationToken cancellationToken)
    {
        context.ChangeTracker.Clear();
        AppendAudit(
            actor,
            action,
            objectId,
            BusinessAuditResult.Denied,
            reasonCode,
            correlationId);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static decimal ResolveActualQuantity(
        TraceabilityMode traceabilityMode,
        string consumptionRule,
        ParsedConsumeCommand command,
        decimal remainingQuantity)
    {
        if (remainingQuantity <= 0)
        {
            throw Rejected(
                "ASSEMBLY_REQUIREMENT_EXCEEDED",
                "本成品对此物料的实际耗用已达到订单快照要求，不能重复采集。",
                409);
        }

        if (traceabilityMode == TraceabilityMode.Serial)
        {
            if (!string.Equals(consumptionRule, "PerProductActual", StringComparison.Ordinal)
                || command.Quantity != 1m)
            {
                throw Rejected(
                    "ASSEMBLY_SERIAL_CAPTURE_INVALID",
                    "序列件必须使用 PerProductActual 规则逐个扫描并耗用 1 个基础单位。",
                    422);
            }

            return 1m;
        }

        if (!string.IsNullOrWhiteSpace(command.ComponentSerialNumber))
        {
            throw Rejected(
                "ASSEMBLY_COMPONENT_SERIAL_NOT_ALLOWED",
                "Lot 件或数量件不得伪造关键部件 SN 关系。",
                422);
        }

        if (string.Equals(consumptionRule, "OrderBackflush", StringComparison.Ordinal))
        {
            if (traceabilityMode != TraceabilityMode.None || command.Quantity is not null)
            {
                throw Rejected(
                    "ASSEMBLY_BACKFLUSH_INVALID",
                    "订单回冲只适用于数量件，数量必须由冻结 BOM 自动计算而不能由操作工填写。",
                    422);
            }

            return remainingQuantity;
        }

        if (!string.Equals(consumptionRule, "PerProductActual", StringComparison.Ordinal)
            || command.Quantity is null or <= 0)
        {
            throw Rejected(
                "ASSEMBLY_ACTUAL_QUANTITY_REQUIRED",
                "逐 SN 实际耗用必须填写正数量，并符合订单快照批准的规则。",
                422);
        }

        return command.Quantity.Value;
    }

    private static TraceabilityMode ParseTraceabilityMode(string value) =>
        Enum.TryParse<TraceabilityMode>(value, ignoreCase: false, out var mode)
            ? mode
            : throw Rejected(
                "ASSEMBLY_TRACEABILITY_INVALID",
                "订单快照包含不支持的追溯粒度。",
                500);

    private static SnapshotRequirement ParseSnapshotRequirement(BomComponentDefinition requirement)
    {
        if (!Required(requirement.AssemblyOperationCode, 80)
            || !Required(requirement.ConsumptionRule, 40))
        {
            throw Rejected(
                "ASSEMBLY_SNAPSHOT_RULE_MISSING",
                "订单执行快照缺少装配工序或耗用规则，不能为旧快照补造执行依据。",
                500);
        }

        return new SnapshotRequirement(
            requirement.AssemblyOperationCode!,
            requirement.ConsumptionRule!,
            ParseTraceabilityMode(requirement.TraceabilityMode));
    }

    private static string DisplayTraceabilityMode(TraceabilityMode mode) =>
        mode == TraceabilityMode.None ? "Quantity" : mode.ToString();

    private static ExecutionTemplateDefinition DeserializeDefinition(string definitionJson) =>
        JsonSerializer.Deserialize<ExecutionTemplateDefinition>(definitionJson, WebJson)
        ?? throw Rejected(
            "EXECUTION_SNAPSHOT_INVALID",
            "订单执行快照无法解析，不能继续装配。",
            500);

    private static string? NextOperationCode(
        ExecutionTemplateDefinition definition,
        string currentOperationCode)
    {
        var operations = definition.Route.Operations.OrderBy(item => item.Sequence).ToArray();
        var index = Array.FindIndex(
            operations,
            item => string.Equals(item.Code, currentOperationCode, StringComparison.Ordinal));
        return index >= 0 && index + 1 < operations.Length ? operations[index + 1].Code : null;
    }

    private static ParsedConsumeCommand Parse(
        AssemblyMaterialConsumeRequest request,
        string serialNumber)
    {
        var sourceSystem = Normalize(request.SourceSystem);
        var idempotencyKey = Normalize(request.IdempotencyKey);
        var operationCode = Normalize(request.OperationCode);
        var materialCode = Normalize(request.MaterialCode);
        var componentSerialNumber = Normalize(request.ComponentSerialNumber);
        var lotNumber = Normalize(request.LotNumber);
        var unit = Normalize(request.Unit);
        var location = Normalize(request.Location);
        if (!Required(sourceSystem, 80)
            || !Required(idempotencyKey, 120)
            || !Required(operationCode, 80)
            || !Required(materialCode, 80)
            || !Required(lotNumber, 120)
            || !Required(unit, 24)
            || !Required(location, 120)
            || request.Quantity is <= 0
            || request.OccurredAtUtc == default)
        {
            throw Rejected(
                "ASSEMBLY_CONSUMPTION_INVALID",
                "装配耗用请求缺少来源、幂等键、工序、物料、Lot、正数量、单位、位置或发生时间。",
                422);
        }

        var quantity = request.Quantity;
        var occurredAtUtc = request.OccurredAtUtc.ToUniversalTime();
        var commandHash = Hash(new
        {
            serialNumber,
            sourceSystem,
            idempotencyKey,
            operationCode,
            materialCode,
            componentSerialNumber,
            lotNumber,
            quantity,
            unit,
            location,
            occurredAtUtc,
        });
        return new ParsedConsumeCommand(
            sourceSystem,
            idempotencyKey,
            operationCode,
            materialCode,
            componentSerialNumber,
            lotNumber,
            quantity,
            unit,
            location,
            occurredAtUtc,
            commandHash);
    }

    private static string Hash(object value) => Convert.ToHexString(
        SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value, WebJson))));

    private static bool Required(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength;

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    private static AssemblyMaterialRejectedException Rejected(
        string code,
        string message,
        int statusCode) => new(code, message, statusCode);

    private sealed record ParsedConsumeCommand(
        string SourceSystem,
        string IdempotencyKey,
        string OperationCode,
        string MaterialCode,
        string ComponentSerialNumber,
        string LotNumber,
        decimal? Quantity,
        string Unit,
        string Location,
        DateTimeOffset OccurredAtUtc,
        string CommandHash);

    private sealed record SnapshotRequirement(
        string OperationCode,
        string ConsumptionRule,
        TraceabilityMode TraceabilityMode);
}
