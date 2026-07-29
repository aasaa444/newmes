using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mes.Domain.Auditing;
using Mes.Domain.Execution;
using Mes.Domain.Identity;
using Mes.Infrastructure.IdentityAccess;
using Mes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Mes.Infrastructure.Execution;

public sealed class ProductIdentityService(
    MesDbContext context,
    IdentityAccessService identityAccess,
    TimeProvider timeProvider)
{
    private const string IdentityObjectType = "ProductIdentity";
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private readonly BusinessAuditWriter auditWriter = new(context, timeProvider);

    public async Task<IdentitySourceRegistrationResult> RegisterSourceAsync(
        EffectiveIdentity actor,
        IdentitySourceRegistrationRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        const string action = "IDENTITY_SOURCE_REGISTER";
        await identityAccess.DemandCapabilityAsync(
            actor,
            BusinessCapability.SystemConfigurationManage,
            action,
            "IdentitySource",
            request.SourceSystem?.Trim() ?? "invalid-source",
            correlationId,
            cancellationToken);
        if (!Required(request.SourceSystem, 80)
            || !Enum.TryParse<IdentitySourceType>(request.SourceType, ignoreCase: true, out var sourceType)
            || !Enum.IsDefined(sourceType)
            || !Required(request.AuthorizedCallerUsername, 80)
            || !Required(request.AuthorizationEvidence, 400)
            || request.AllowedIdentifierTypes is not { Count: > 0 })
        {
            throw Rejected(
                "IDENTITY_SOURCE_INVALID",
                "身份来源配置不完整；请检查来源类型、授权调用账号、允许标识类型和授权依据。",
                422);
        }

        var identifierTypes = request.AllowedIdentifierTypes
            .Select(ParseIdentifierType)
            .Distinct()
            .ToArray();
        if (identifierTypes.Length != request.AllowedIdentifierTypes.Count)
        {
            throw Rejected(
                "IDENTITY_SOURCE_INVALID",
                "身份来源的允许标识类型不能为空或重复。",
                422);
        }

        var sourceSystem = Normalize(request.SourceSystem);
        var callerUsername = Normalize(request.AuthorizedCallerUsername);
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            if (await context.IdentitySourceRegistrations.AnyAsync(
                    source => source.SourceSystem == sourceSystem,
                    cancellationToken))
            {
                throw Rejected(
                    "IDENTITY_SOURCE_ALREADY_EXISTS",
                    "该身份来源已经注册；如需变更授权，请使用后续受控版本变更流程。",
                    409);
            }

            var caller = await context.UserAccounts.SingleOrDefaultAsync(
                user => user.Username == callerUsername && user.IsActive,
                cancellationToken)
                ?? throw Rejected(
                    "IDENTITY_SOURCE_CALLER_NOT_FOUND",
                    "授权调用账号不存在或已停用；请先建立专用受控账号。",
                    422);
            var source = new IdentitySourceRegistration
            {
                Id = Guid.NewGuid(),
                SourceSystem = sourceSystem,
                SourceType = sourceType,
                IsDemo = sourceType == IdentitySourceType.DemoControlledPool,
                IsActive = true,
                AuthorizationEvidence = Normalize(request.AuthorizationEvidence),
                AuthorizedCallerUserId = caller.Id,
                RegisteredByUserId = actor.UserId,
                RegisteredAtUtc = timeProvider.GetUtcNow(),
            };
            foreach (var identifierType in identifierTypes)
            {
                source.IdentifierGrants.Add(new IdentitySourceIdentifierGrant
                {
                    IdentitySourceRegistrationId = source.Id,
                    IdentifierType = identifierType,
                });
            }

            context.IdentitySourceRegistrations.Add(source);
            AppendAudit(
                actor,
                BusinessRole.SystemAdministrator,
                BusinessCapability.SystemConfigurationManage,
                action,
                source.Id.ToString(),
                BusinessAuditResult.Succeeded,
                null,
                correlationId);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new IdentitySourceRegistrationResult(
                source.Id,
                source.SourceSystem,
                source.SourceType.ToString(),
                caller.Username,
                identifierTypes.Select(type => type.ToString()).ToArray(),
                source.IsDemo,
                "Registered");
        });
    }

    public async Task<ProductIdentityAllocationResult> AllocateAsync(
        EffectiveIdentity actor,
        ProductIdentityAllocationRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        const string action = "PRODUCT_IDENTITY_ALLOCATE";
        var objectId = $"{request.SourceSystem?.Trim()}:{request.IdempotencyKey?.Trim()}";
        await identityAccess.DemandCapabilityAsync(
            actor,
            BusinessCapability.SystemConfigurationManage,
            action,
            IdentityObjectType,
            objectId,
            correlationId,
            cancellationToken);

        var proposed = ParseAllocation(request);
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            var existing = await context.ProductIdentities
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    identity => identity.AllocationSourceSystem == proposed.SourceSystem
                        && identity.AllocationIdempotencyKey == proposed.IdempotencyKey,
                    cancellationToken);
            if (existing is not null)
            {
                if (!string.Equals(
                        existing.AllocationCommandHash,
                        proposed.CommandHash,
                        StringComparison.Ordinal))
                {
                    throw Rejected(
                        "IDENTITY_IDEMPOTENCY_CONFLICT",
                        "相同来源和幂等键已经对应另一份身份数据；请核对原请求，不得覆盖既有身份。",
                        409);
                }

                await transaction.CommitAsync(cancellationToken);
                return AllocationResult(existing, isReplay: true);
            }

            var material = await context.Materials.SingleOrDefaultAsync(
                item => item.Code == proposed.MaterialCode && item.IsActive,
                cancellationToken)
                ?? throw Rejected(
                    "IDENTITY_MATERIAL_NOT_FOUND",
                    "身份对应的成品物料不存在或已停用；请先核对 ERP/主数据编码。",
                    422);
            var sourceSystems = proposed.Identifiers
                .Select(item => item.SourceSystem)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var registeredSources = await context.IdentitySourceRegistrations
                .Include(source => source.IdentifierGrants)
                .Where(source => sourceSystems.Contains(source.SourceSystem))
                .ToArrayAsync(cancellationToken);
            foreach (var identifier in proposed.Identifiers)
            {
                var registered = registeredSources.SingleOrDefault(
                    source => source.SourceSystem == identifier.SourceSystem);
                if (registered is null
                    || !registered.IsActive
                    || registered.AuthorizedCallerUserId != actor.UserId
                    || registered.SourceType != identifier.SourceType
                    || registered.IsDemo != identifier.IsDemo
                    || !registered.IdentifierGrants.Any(
                        grant => grant.IdentifierType == identifier.Type))
                {
                    throw Rejected(
                        "IDENTITY_SOURCE_NOT_AUTHORIZED",
                        "标识来源未注册、已停用、调用账号不匹配或无权分配该标识类型；请核对授权适配器配置。",
                        403);
                }

                if (await context.ControlledIdentifiers.AsNoTracking().AnyAsync(
                        existing => existing.Type == identifier.Type
                            && existing.Value == identifier.Value,
                        cancellationToken))
                {
                    throw Rejected(
                        "IDENTITY_VALUE_ALREADY_EXISTS",
                        "该 SN、MAC、IMEI 或证书标识已经登记；请查询原身份，不得重复分配。",
                        409);
                }
            }
            var now = timeProvider.GetUtcNow();
            var identityId = Guid.NewGuid();
            var serial = proposed.Identifiers.Single(item => item.Type == ControlledIdentifierType.SerialNumber);
            var identity = new ProductIdentity
            {
                Id = identityId,
                MaterialId = material.Id,
                SerialNumber = serial.Value,
                SerialSourceType = serial.SourceType,
                SerialSourceSystem = serial.SourceSystem,
                SerialSourceReference = serial.SourceReference,
                IsDemo = proposed.Identifiers.Any(item => item.IsDemo),
                Status = ProductIdentityStatus.Allocated,
                AllocatedAtUtc = now,
                AllocationSourceSystem = proposed.SourceSystem,
                AllocationIdempotencyKey = proposed.IdempotencyKey,
                AllocationCommandHash = proposed.CommandHash,
            };
            context.ProductIdentities.Add(identity);
            context.ControlledIdentifiers.AddRange(proposed.Identifiers.Select(item =>
                new ControlledIdentifier
                {
                    Id = Guid.NewGuid(),
                    ProductIdentityId = identityId,
                    Type = item.Type,
                    Value = item.Value,
                    SourceType = item.SourceType,
                    SourceSystem = item.SourceSystem,
                    SourceReference = item.SourceReference,
                    IsDemo = item.IsDemo,
                }));
            AppendManufacturingEvent(
                "IDENTITY_ALLOCATED",
                identity,
                actor,
                now,
                now,
                location: null,
                correlationId,
                new
                {
                    identity.SerialNumber,
                    serialSourceType = identity.SerialSourceType.ToString(),
                    identity.SerialSourceSystem,
                    identity.SerialSourceReference,
                    identity.IsDemo,
                    identifiers = proposed.Identifiers.Select(item => new
                    {
                        type = item.Type.ToString(),
                        item.Value,
                        sourceType = item.SourceType.ToString(),
                        item.SourceSystem,
                        item.SourceReference,
                        item.IsDemo,
                    }),
                });
            AppendAudit(
                actor,
                BusinessRole.SystemAdministrator,
                BusinessCapability.SystemConfigurationManage,
                action,
                identity.Id.ToString(),
                BusinessAuditResult.Succeeded,
                null,
                correlationId);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return AllocationResult(identity, isReplay: false);
        });
    }

    public async Task<StartWipResult> StartWipAsync(
        EffectiveIdentity actor,
        Guid productionOrderId,
        StartWipRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        const string action = "START_WIP";
        await identityAccess.DemandCapabilityAsync(
            actor,
            BusinessCapability.StationExecute,
            action,
            "ProductionOrder",
            productionOrderId.ToString(),
            correlationId,
            cancellationToken);
        ValidateStartRequest(request);
        var sourceSystem = Normalize(request.SourceSystem);
        var idempotencyKey = Normalize(request.IdempotencyKey);
        var location = Normalize(request.Location);
        var commandHash = Hash(new
        {
            productionOrderId,
            sourceSystem,
            idempotencyKey,
            request.ProductIdentityId,
            location,
            occurredAtUtc = request.OccurredAtUtc.ToUniversalTime(),
        });

        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            var replay = await context.StartWipCommandReceipts
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    receipt => receipt.SourceSystem == sourceSystem
                        && receipt.IdempotencyKey == idempotencyKey,
                    cancellationToken);
            if (replay is not null)
            {
                if (!string.Equals(replay.CommandHash, commandHash, StringComparison.Ordinal))
                {
                    throw Rejected(
                        "START_WIP_IDEMPOTENCY_CONFLICT",
                        "相同来源和幂等键已经对应另一条投产命令；请核对原请求。",
                        409);
                }

                await transaction.CommitAsync(cancellationToken);
                return StartResult(replay, isReplay: true);
            }

            var order = await context.ProductionOrders.SingleOrDefaultAsync(
                item => item.Id == productionOrderId,
                cancellationToken)
                ?? throw Rejected(
                    "PRODUCTION_ORDER_NOT_FOUND",
                    "生产订单不存在；请刷新工位任务后重试。",
                    404);
            if (order.Status is not (ProductionOrderStatus.Released or ProductionOrderStatus.InProduction))
            {
                throw Rejected(
                    "START_WIP_ORDER_STATUS_BLOCKED",
                    "该订单未下达、已暂停或已进入终态，不能投入新产品；请联系计划员处理订单状态。",
                    409);
            }

            if (order.StartedQuantity >= order.PlannedQuantity)
            {
                throw Rejected(
                    "START_WIP_PLANNED_QUANTITY_EXCEEDED",
                    "订单投产数已达到计划数量，禁止超投；请由计划员先完成受控订单变更。",
                    409);
            }

            var identity = await context.ProductIdentities
                .Include(item => item.ProductionOrder)
                .SingleOrDefaultAsync(item => item.Id == request.ProductIdentityId, cancellationToken)
                ?? throw Rejected(
                    "PRODUCT_IDENTITY_NOT_FOUND",
                    "未找到该产品身份；请核对扫描值或先由授权来源登记身份。",
                    404);
            if (identity.Status != ProductIdentityStatus.Allocated)
            {
                throw Rejected(
                    "PRODUCT_IDENTITY_NOT_AVAILABLE",
                    "该产品身份已绑定或已作废，不能再次投产；请查询身份履历并处理原绑定。",
                    409);
            }

            if (identity.MaterialId != order.MaterialId)
            {
                throw Rejected(
                    "PRODUCT_IDENTITY_MATERIAL_MISMATCH",
                    "该身份所属产品与订单产品不一致；请停止投产并核对成品编码。",
                    422);
            }

            var snapshot = await context.ProductionOrderExecutionSnapshots
                .SingleOrDefaultAsync(item => item.ProductionOrderId == order.Id, cancellationToken)
                ?? throw Rejected(
                    "EXECUTION_SNAPSHOT_NOT_FOUND",
                    "订单没有冻结的执行快照，不能投产；请联系计划员重新核对下达结果。",
                    409);
            var definition = JsonSerializer.Deserialize<ExecutionTemplateDefinition>(
                snapshot.DefinitionJson,
                WebJson)
                ?? throw Rejected(
                    "EXECUTION_SNAPSHOT_INVALID",
                    "订单执行快照无法解析，不能投产；请联系系统管理员核对版本证据。",
                    500);
            ValidateSnapshotPolicy(
                identity,
                order,
                request.OccurredAtUtc.ToUniversalTime(),
                definition.IdentityPolicy,
                await context.ControlledIdentifiers
                    .AsNoTracking()
                    .Where(item => item.ProductIdentityId == identity.Id)
                    .ToArrayAsync(cancellationToken));
            var operations = definition.Route.Operations.OrderBy(item => item.Sequence).ToArray();
            if (operations.Length == 0
                || !string.Equals(operations[0].Code, "START_WIP", StringComparison.OrdinalIgnoreCase))
            {
                throw Rejected(
                    "START_WIP_ROUTE_INVALID",
                    "订单路线的首个制造事件不是 START_WIP；请由工艺工程师修正新版本，不能现场跳站。",
                    422);
            }

            var now = timeProvider.GetUtcNow();
            identity.Status = ProductIdentityStatus.Bound;
            identity.ProductionOrderId = order.Id;
            identity.ExecutionSnapshotId = snapshot.Id;
            identity.NextOperationCode = operations.Skip(1).FirstOrDefault()?.Code;
            identity.BoundAtUtc = now;
            identity.StartSourceSystem = sourceSystem;
            identity.StartIdempotencyKey = idempotencyKey;
            identity.StartCommandHash = commandHash;
            order.StartedQuantity++;
            if (order.Status == ProductionOrderStatus.Released)
            {
                order.Status = ProductionOrderStatus.InProduction;
            }

            var bindingEvent = AppendManufacturingEvent(
                "IDENTITY_BOUND",
                identity,
                actor,
                request.OccurredAtUtc.ToUniversalTime(),
                now,
                location,
                correlationId,
                new
                {
                    productionOrderId = order.Id,
                    order.OrderNumber,
                    executionSnapshotId = snapshot.Id,
                    snapshot.SnapshotVersion,
                });
            AppendManufacturingEvent(
                "START_WIP",
                identity,
                actor,
                request.OccurredAtUtc.ToUniversalTime(),
                now,
                location,
                correlationId,
                new
                {
                    productionOrderId = order.Id,
                    order.OrderNumber,
                    identity.SerialNumber,
                    nextOperationCode = identity.NextOperationCode,
                    startedQuantity = order.StartedQuantity,
                },
                bindingEvent.Id);
            AppendAudit(
                actor,
                BusinessRole.Operator,
                BusinessCapability.StationExecute,
                action,
                identity.Id.ToString(),
                BusinessAuditResult.Succeeded,
                null,
                correlationId);
            context.StartWipCommandReceipts.Add(new StartWipCommandReceipt
            {
                Id = Guid.NewGuid(),
                SourceSystem = sourceSystem,
                IdempotencyKey = idempotencyKey,
                CommandHash = commandHash,
                ProductIdentityId = identity.Id,
                ProductionOrderId = order.Id,
                SerialNumber = identity.SerialNumber,
                ProductionOrderNumber = order.OrderNumber,
                OrderStatus = order.Status.ToString(),
                StartedQuantity = order.StartedQuantity,
                IdentitySourceSystem = identity.SerialSourceSystem,
                IdentitySourceType = identity.SerialSourceType.ToString(),
                IsDemo = identity.IsDemo,
                NextOperationCode = identity.NextOperationCode,
                CompletedAtUtc = now,
            });
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return StartResult(identity, order, isReplay: false);
        });
    }

    public async Task<ProductIdentityWorkstationResult> ReadWorkstationAsync(
        EffectiveIdentity actor,
        string serialNumber,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var normalizedSerial = Normalize(serialNumber);
        await identityAccess.DemandCapabilityAsync(
            actor,
            BusinessCapability.StationExecute,
            "PRODUCT_IDENTITY_WORKSTATION_READ",
            IdentityObjectType,
            normalizedSerial,
            correlationId,
            cancellationToken);
        var identity = await context.ProductIdentities
            .AsNoTracking()
            .Include(item => item.ProductionOrder)
            .Include(item => item.ExecutionSnapshot)
            .SingleOrDefaultAsync(item => item.SerialNumber == normalizedSerial, cancellationToken)
            ?? throw Rejected(
                "PRODUCT_IDENTITY_NOT_FOUND",
                "未找到该产品身份；请核对扫描值或先由授权来源登记身份。",
                404);
        var identifiers = await context.ControlledIdentifiers
            .AsNoTracking()
            .Where(item => item.ProductIdentityId == identity.Id)
            .OrderBy(item => item.Type)
            .Select(item => new ControlledIdentifierResult(
                item.Type.ToString(),
                item.Value,
                item.SourceType.ToString(),
                item.SourceSystem,
                item.SourceReference,
                item.IsDemo))
            .ToArrayAsync(cancellationToken);
        return new ProductIdentityWorkstationResult(
            identity.Id,
            identity.SerialNumber,
            identity.Status.ToString(),
            identity.SerialSourceSystem,
            identity.SerialSourceType.ToString(),
            identity.SerialSourceReference,
            identity.IsDemo,
            identity.ProductionOrderId,
            identity.ProductionOrder?.OrderNumber,
            identity.ExecutionSnapshot?.SnapshotVersion,
            identity.NextOperationCode,
            identifiers);
    }

    public async Task<ProductLabelCommandResult> PrintLabelAsync(
        EffectiveIdentity actor,
        Guid identityId,
        ProductLabelPrintRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        ValidateLabelFields(request.TemplateVersion, request.Printer, request.OccurredAtUtc);
        return await ExecuteLabelAsync(
            actor,
            identityId,
            "LABEL_PRINTED",
            correlationId,
            async (identity, now) =>
            {
                var label = new ProductLabel
                {
                    Id = Guid.NewGuid(),
                    ProductIdentityId = identity.Id,
                    TemplateVersion = Normalize(request.TemplateVersion),
                    Printer = Normalize(request.Printer),
                    Status = ProductLabelStatus.Active,
                    CreatedAtUtc = now,
                };
                context.ProductLabels.Add(label);
                AppendManufacturingEvent(
                    "LABEL_PRINTED",
                    identity,
                    actor,
                    request.OccurredAtUtc.ToUniversalTime(),
                    now,
                    request.Printer,
                    correlationId,
                    new { labelId = label.Id, label.TemplateVersion, label.Printer });
                return new ProductLabelCommandResult(
                    label.Id,
                    identity.Id,
                    label.Status.ToString(),
                    "LABEL_PRINTED");
            },
            cancellationToken);
    }

    public async Task<ProductLabelCommandResult> ReprintLabelAsync(
        EffectiveIdentity actor,
        Guid identityId,
        Guid labelId,
        ProductLabelReasonRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        ValidateReason(request.Reason, request.OccurredAtUtc);
        return await ExecuteLabelAsync(
            actor,
            identityId,
            "LABEL_REPRINTED",
            correlationId,
            async (identity, now) =>
            {
                var label = await ReadActiveLabelAsync(identityId, labelId, cancellationToken);
                AppendManufacturingEvent(
                    "LABEL_REPRINTED",
                    identity,
                    actor,
                    request.OccurredAtUtc.ToUniversalTime(),
                    now,
                    label.Printer,
                    correlationId,
                    new
                    {
                        labelId = label.Id,
                        label.TemplateVersion,
                        label.Printer,
                        reason = Normalize(request.Reason),
                    });
                return new ProductLabelCommandResult(
                    label.Id,
                    identity.Id,
                    label.Status.ToString(),
                    "LABEL_REPRINTED");
            },
            cancellationToken);
    }

    public async Task<ProductLabelCommandResult> VoidLabelAsync(
        EffectiveIdentity actor,
        Guid identityId,
        Guid labelId,
        ProductLabelReasonRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        ValidateReason(request.Reason, request.OccurredAtUtc);
        return await ExecuteLabelAsync(
            actor,
            identityId,
            "LABEL_VOIDED",
            correlationId,
            async (identity, now) =>
            {
                var label = await ReadActiveLabelAsync(identityId, labelId, cancellationToken);
                label.Status = ProductLabelStatus.Voided;
                AppendManufacturingEvent(
                    "LABEL_VOIDED",
                    identity,
                    actor,
                    request.OccurredAtUtc.ToUniversalTime(),
                    now,
                    label.Printer,
                    correlationId,
                    new { labelId = label.Id, reason = Normalize(request.Reason) });
                return new ProductLabelCommandResult(
                    label.Id,
                    identity.Id,
                    label.Status.ToString(),
                    "LABEL_VOIDED");
            },
            cancellationToken);
    }

    public async Task<ProductLabelCommandResult> ReplaceLabelAsync(
        EffectiveIdentity actor,
        Guid identityId,
        Guid labelId,
        ProductLabelReplaceRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        ValidateLabelFields(request.TemplateVersion, request.Printer, request.OccurredAtUtc);
        ValidateReason(request.Reason, request.OccurredAtUtc);
        return await ExecuteLabelAsync(
            actor,
            identityId,
            "LABEL_REPLACED",
            correlationId,
            async (identity, now) =>
            {
                var original = await ReadActiveLabelAsync(identityId, labelId, cancellationToken);
                original.Status = ProductLabelStatus.Replaced;
                var replacement = new ProductLabel
                {
                    Id = Guid.NewGuid(),
                    ProductIdentityId = identity.Id,
                    TemplateVersion = Normalize(request.TemplateVersion),
                    Printer = Normalize(request.Printer),
                    Status = ProductLabelStatus.Active,
                    ReplacesLabelId = original.Id,
                    CreatedAtUtc = now,
                };
                context.ProductLabels.Add(replacement);
                AppendManufacturingEvent(
                    "LABEL_REPLACED",
                    identity,
                    actor,
                    request.OccurredAtUtc.ToUniversalTime(),
                    now,
                    replacement.Printer,
                    correlationId,
                    new
                    {
                        originalLabelId = original.Id,
                        replacementLabelId = replacement.Id,
                        replacement.TemplateVersion,
                        replacement.Printer,
                        reason = Normalize(request.Reason),
                    });
                return new ProductLabelCommandResult(
                    replacement.Id,
                    identity.Id,
                    replacement.Status.ToString(),
                    "LABEL_REPLACED");
            },
            cancellationToken);
    }

    public Task<ProductIdentityCommandResult> UnbindAsync(
        EffectiveIdentity actor,
        Guid identityId,
        ProductIdentityCorrectionRequest request,
        string correlationId,
        CancellationToken cancellationToken = default) => ExecuteIdentityCorrectionAsync(
            actor,
            identityId,
            request,
            "IDENTITY_UNBOUND",
            correlationId,
            cancellationToken);

    public Task<ProductIdentityCommandResult> VoidAsync(
        EffectiveIdentity actor,
        Guid identityId,
        ProductIdentityCorrectionRequest request,
        string correlationId,
        CancellationToken cancellationToken = default) => ExecuteIdentityCorrectionAsync(
            actor,
            identityId,
            request,
            "IDENTITY_VOIDED",
            correlationId,
            cancellationToken);

    private async Task<ProductLabelCommandResult> ExecuteLabelAsync(
        EffectiveIdentity actor,
        Guid identityId,
        string action,
        string correlationId,
        Func<ProductIdentity, DateTimeOffset, Task<ProductLabelCommandResult>> apply,
        CancellationToken cancellationToken)
    {
        await DemandStationAsync(actor, action, identityId.ToString(), correlationId, cancellationToken);
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            var identity = await context.ProductIdentities.SingleOrDefaultAsync(
                item => item.Id == identityId,
                cancellationToken)
                ?? throw IdentityNotFound();
            if (identity.Status == ProductIdentityStatus.Voided)
            {
                throw Rejected(
                    "PRODUCT_IDENTITY_VOIDED",
                    "该产品身份已作废，不能继续打印或更换标签；请查询身份履历。",
                    409);
            }

            var result = await apply(identity, timeProvider.GetUtcNow());
            AppendAudit(
                actor,
                BusinessRole.Operator,
                BusinessCapability.StationExecute,
                action,
                identity.Id.ToString(),
                BusinessAuditResult.Succeeded,
                null,
                correlationId);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        });
    }

    private async Task<ProductIdentityCommandResult> ExecuteIdentityCorrectionAsync(
        EffectiveIdentity actor,
        Guid identityId,
        ProductIdentityCorrectionRequest request,
        string action,
        string correlationId,
        CancellationToken cancellationToken)
    {
        ValidateCorrection(request);
        await DemandStationAsync(actor, action, identityId.ToString(), correlationId, cancellationToken);
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            var identity = await context.ProductIdentities
                .Include(item => item.ProductionOrder)
                .SingleOrDefaultAsync(item => item.Id == identityId, cancellationToken)
                ?? throw IdentityNotFound();
            var now = timeProvider.GetUtcNow();
            if (action == "IDENTITY_VOIDED")
            {
                if (identity.Status != ProductIdentityStatus.Allocated)
                {
                    throw Rejected(
                        "PRODUCT_IDENTITY_CANNOT_VOID",
                        "只有尚未绑定的身份可以作废；已投产身份必须先执行受控解绑。",
                        409);
                }

                identity.Status = ProductIdentityStatus.Voided;
                AppendManufacturingEvent(
                    action,
                    identity,
                    actor,
                    request.OccurredAtUtc.ToUniversalTime(),
                    now,
                    request.Location,
                    correlationId,
                    new { reason = Normalize(request.Reason) });
            }
            else
            {
                if (identity.Status != ProductIdentityStatus.Bound
                    || identity.ProductionOrder is null)
                {
                    throw Rejected(
                        "PRODUCT_IDENTITY_NOT_BOUND",
                        "该身份当前没有有效工单绑定，无需解绑；请查询身份履历。",
                        409);
                }

                var downstreamExists = await context.ManufacturingEvents.AsNoTracking().AnyAsync(
                    item => item.ProductIdentityId == identity.Id
                        && item.EventType != "IDENTITY_ALLOCATED"
                        && item.EventType != "IDENTITY_BOUND"
                        && item.EventType != "START_WIP"
                        && !item.EventType.StartsWith("LABEL_"),
                    cancellationToken);
                if (downstreamExists)
                {
                    throw Rejected(
                        "PRODUCT_IDENTITY_UNBIND_BLOCKED",
                        "该产品已有后续制造事实，不能直接解绑；请进入受控返工或质量处置流程。",
                        409);
                }

                var order = identity.ProductionOrder;
                var startEvent = await context.ManufacturingEvents.AsNoTracking()
                    .Where(item => item.ProductIdentityId == identity.Id && item.EventType == "START_WIP")
                    .OrderByDescending(item => item.RecordedAtUtc)
                    .FirstAsync(cancellationToken);
                var unbound = AppendManufacturingEvent(
                    "IDENTITY_UNBOUND",
                    identity,
                    actor,
                    request.OccurredAtUtc.ToUniversalTime(),
                    now,
                    request.Location,
                    correlationId,
                    new
                    {
                        productionOrderId = order.Id,
                        reason = Normalize(request.Reason),
                    });
                AppendManufacturingEvent(
                    "START_WIP_REVERSED",
                    identity,
                    actor,
                    request.OccurredAtUtc.ToUniversalTime(),
                    now,
                    request.Location,
                    correlationId,
                    new
                    {
                        productionOrderId = order.Id,
                        reason = Normalize(request.Reason),
                        correctsEventId = startEvent.Id,
                    },
                    unbound.Id,
                    startEvent.Id);
                order.StartedQuantity--;
                if (order.StartedQuantity == 0 && order.Status == ProductionOrderStatus.InProduction)
                {
                    order.Status = ProductionOrderStatus.Released;
                }

                identity.Status = ProductIdentityStatus.Allocated;
                identity.ProductionOrderId = null;
                identity.ProductionOrder = null;
                identity.ExecutionSnapshotId = null;
                identity.NextOperationCode = null;
                identity.BoundAtUtc = null;
                identity.StartSourceSystem = null;
                identity.StartIdempotencyKey = null;
                identity.StartCommandHash = null;
            }

            AppendAudit(
                actor,
                BusinessRole.Operator,
                BusinessCapability.StationExecute,
                action,
                identity.Id.ToString(),
                BusinessAuditResult.Succeeded,
                null,
                correlationId);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new ProductIdentityCommandResult(
                identity.Id,
                identity.SerialNumber,
                identity.Status.ToString(),
                identity.ProductionOrderId);
        });
    }

    private async Task<ProductLabel> ReadActiveLabelAsync(
        Guid identityId,
        Guid labelId,
        CancellationToken cancellationToken)
    {
        var label = await context.ProductLabels.SingleOrDefaultAsync(
            item => item.Id == labelId && item.ProductIdentityId == identityId,
            cancellationToken)
            ?? throw Rejected(
                "PRODUCT_LABEL_NOT_FOUND",
                "未找到该身份对应的标签记录；请刷新标签历史后重试。",
                404);
        if (label.Status != ProductLabelStatus.Active)
        {
            throw Rejected(
                "PRODUCT_LABEL_NOT_ACTIVE",
                "该标签已作废或已被替换，不能重复操作；请使用当前有效标签。",
                409);
        }

        return label;
    }

    private Task DemandStationAsync(
        EffectiveIdentity actor,
        string action,
        string objectId,
        string correlationId,
        CancellationToken cancellationToken) => identityAccess.DemandCapabilityAsync(
            actor,
            BusinessCapability.StationExecute,
            action,
            IdentityObjectType,
            objectId,
            correlationId,
            cancellationToken);

    private ManufacturingEvent AppendManufacturingEvent(
        string eventType,
        ProductIdentity identity,
        EffectiveIdentity actor,
        DateTimeOffset occurredAtUtc,
        DateTimeOffset recordedAtUtc,
        string? location,
        string correlationId,
        object payload,
        Guid? causationEventId = null,
        Guid? correctsEventId = null)
    {
        var manufacturingEvent = new ManufacturingEvent
        {
            Id = Guid.NewGuid(),
            EventType = eventType,
            AggregateType = IdentityObjectType,
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
        BusinessRole authorizedRole,
        BusinessCapability capability,
        string action,
        string objectId,
        BusinessAuditResult result,
        string? reasonCode,
        string correlationId) => auditWriter.Append(new BusinessAuditWrite(
            BusinessAuditActor.From(actor),
            authorizedRole,
            capability,
            action,
            IdentityObjectType,
            objectId,
            result,
            reasonCode,
            correlationId));

    private static ProposedAllocation ParseAllocation(ProductIdentityAllocationRequest request)
    {
        if (!Required(request.SourceSystem, 80)
            || !Required(request.IdempotencyKey, 120)
            || !Required(request.MaterialCode, 80)
            || request.Identifiers is not { Count: > 0 })
        {
            throw InvalidAllocation();
        }

        var identifiers = request.Identifiers.Select(ParseIdentifier).ToArray();
        if (identifiers.Count(item => item.Type == ControlledIdentifierType.SerialNumber) != 1
            || identifiers.Select(item => item.Type).Distinct().Count() != identifiers.Length)
        {
            throw InvalidAllocation();
        }

        return new ProposedAllocation(
            Normalize(request.SourceSystem),
            Normalize(request.IdempotencyKey),
            Normalize(request.MaterialCode),
            identifiers,
            Hash(request));
    }

    private static ProposedIdentifier ParseIdentifier(ControlledIdentifierRequest request)
    {
        if (!Enum.TryParse<ControlledIdentifierType>(request.Type, ignoreCase: true, out var type)
            || !Enum.IsDefined(type)
            || !Enum.TryParse<IdentitySourceType>(request.SourceType, ignoreCase: true, out var sourceType)
            || !Enum.IsDefined(sourceType)
            || !Required(request.Value, 200)
            || !Required(request.SourceSystem, 80)
            || !Required(request.SourceReference, 200))
        {
            throw InvalidAllocation();
        }

        return new ProposedIdentifier(
            type,
            NormalizeIdentifier(type, request.Value),
            sourceType,
            Normalize(request.SourceSystem),
            Normalize(request.SourceReference),
            sourceType == IdentitySourceType.DemoControlledPool);
    }

    private static ControlledIdentifierType ParseIdentifierType(string value)
    {
        if (!Enum.TryParse<ControlledIdentifierType>(value, ignoreCase: true, out var type)
            || !Enum.IsDefined(type))
        {
            throw Rejected(
                "IDENTITY_SOURCE_INVALID",
                "身份来源包含不支持的标识类型。",
                422);
        }

        return type;
    }

    private static void ValidateStartRequest(StartWipRequest request)
    {
        if (!Required(request.SourceSystem, 80)
            || !Required(request.IdempotencyKey, 120)
            || request.ProductIdentityId == Guid.Empty
            || !Required(request.Location, 120)
            || request.OccurredAtUtc == default)
        {
            throw Rejected(
                "START_WIP_INVALID",
                "投产命令字段不完整；请检查来源、幂等键、产品身份、位置和发生时间。",
                422);
        }
    }

    private static void ValidateLabelFields(
        string templateVersion,
        string printer,
        DateTimeOffset occurredAtUtc)
    {
        if (!Required(templateVersion, 80)
            || !Required(printer, 160)
            || occurredAtUtc == default)
        {
            throw Rejected(
                "PRODUCT_LABEL_INVALID",
                "标签命令字段不完整；请检查模板版本、打印设备和发生时间。",
                422);
        }
    }

    private static void ValidateReason(string reason, DateTimeOffset occurredAtUtc)
    {
        if (!Required(reason, 400) || occurredAtUtc == default)
        {
            throw Rejected(
                "PRODUCT_LABEL_REASON_REQUIRED",
                "补打、作废或换标必须填写原因和发生时间。",
                422);
        }
    }

    private static void ValidateCorrection(ProductIdentityCorrectionRequest request)
    {
        if (!Required(request.Reason, 400)
            || !Required(request.Location, 120)
            || request.OccurredAtUtc == default)
        {
            throw Rejected(
                "IDENTITY_CORRECTION_INVALID",
                "身份解绑或作废必须填写原因、位置和发生时间。",
                422);
        }
    }

    private static void ValidateSnapshotPolicy(
        ProductIdentity identity,
        ProductionOrder order,
        DateTimeOffset occurredAtUtc,
        IdentityPolicyDefinition policy,
        IReadOnlyCollection<ControlledIdentifier> identifiers)
    {
        var allowedSources = policy.FinishedSerialSource.Trim() switch
        {
            "ERP" => new[] { IdentitySourceType.Erp },
            "LabelSystem" => new[] { IdentitySourceType.LabelSystem },
            "MesControlledPool" => new[] { IdentitySourceType.MesControlledPool },
            "DemoControlledPool" => new[] { IdentitySourceType.DemoControlledPool },
            "AuthorizedExternalOrControlledPool" => new[]
            {
                IdentitySourceType.Erp,
                IdentitySourceType.LabelSystem,
                IdentitySourceType.MesControlledPool,
            },
            _ => [],
        };
        if (!allowedSources.Contains(identity.SerialSourceType))
        {
            throw Rejected(
                "IDENTITY_SOURCE_POLICY_MISMATCH",
                "该 SN 来源不符合订单冻结的身份策略；请更换身份或由计划/工艺人员核对快照版本。",
                422);
        }

        if (identity.IsDemo
            && identity.SerialSourceType != IdentitySourceType.DemoControlledPool)
        {
            throw Rejected(
                "IDENTITY_DEMO_NOT_ALLOWED",
                "该身份混入演示标识，不能用于生产来源策略；请全部改用授权生产来源或使用明确的演示订单策略。",
                422);
        }

        if (identity.IsDemo
            && !string.Equals(
                policy.FinishedSerialSource,
                "DemoControlledPool",
                StringComparison.Ordinal))
        {
            throw Rejected(
                "IDENTITY_DEMO_NOT_ALLOWED",
                "演示身份不能进入生产订单；请使用明确配置为 DemoControlledPool 的演示策略。",
                422);
        }

        var timingValid = policy.AllocationTiming switch
        {
            "AtOrderRelease" => order.ReleasedAtUtc is not null
                && identity.AllocatedAtUtc <= order.ReleasedAtUtc,
            "BeforeStartWip" or "OnDemandBeforeStartWip" =>
                identity.AllocatedAtUtc <= occurredAtUtc,
            _ => false,
        };
        if (!timingValid)
        {
            throw Rejected(
                "IDENTITY_ALLOCATION_TIMING_MISMATCH",
                "该身份的分配时间不符合订单冻结策略；请按策略重新取得身份，不能在投产后补录。",
                422);
        }

        var existingTypes = identifiers.Select(item => item.Type).ToHashSet();
        foreach (var required in policy.RequiredIdentifiers)
        {
            var normalized = string.Equals(required, "MAC", StringComparison.OrdinalIgnoreCase)
                ? nameof(ControlledIdentifierType.MacAddress)
                : required;
            if (!Enum.TryParse<ControlledIdentifierType>(normalized, ignoreCase: true, out var type)
                || !existingTypes.Contains(type))
            {
                throw Rejected(
                    "IDENTITY_REQUIRED_IDENTIFIER_MISSING",
                    $"该身份缺少订单快照要求的标识 {required}；请从授权系统或号池补齐后再投产。",
                    422);
            }
        }
    }

    private static ProductIdentityAllocationResult AllocationResult(
        ProductIdentity identity,
        bool isReplay) => new(
            identity.Id,
            identity.SerialNumber,
            identity.SerialSourceType.ToString(),
            identity.SerialSourceSystem,
            identity.SerialSourceReference,
            identity.IsDemo,
            identity.Status.ToString(),
            isReplay);

    private static StartWipResult StartResult(
        ProductIdentity identity,
        ProductionOrder order,
        bool isReplay) => new(
            identity.Id,
            identity.SerialNumber,
            order.Id,
            order.OrderNumber,
            order.Status.ToString(),
            order.StartedQuantity,
            identity.SerialSourceSystem,
            identity.SerialSourceType.ToString(),
            identity.IsDemo,
            identity.NextOperationCode,
            isReplay);

    private static StartWipResult StartResult(
        StartWipCommandReceipt receipt,
        bool isReplay) => new(
            receipt.ProductIdentityId,
            receipt.SerialNumber,
            receipt.ProductionOrderId,
            receipt.ProductionOrderNumber,
            receipt.OrderStatus,
            receipt.StartedQuantity,
            receipt.IdentitySourceSystem,
            receipt.IdentitySourceType,
            receipt.IsDemo,
            receipt.NextOperationCode,
            isReplay);

    private static string NormalizeIdentifier(ControlledIdentifierType type, string value) =>
        type is ControlledIdentifierType.MacAddress or ControlledIdentifierType.Imei
            ? Normalize(value).ToUpperInvariant()
            : Normalize(value);

    private static bool Required(string? value, int maxLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length <= maxLength;

    private static string Normalize(string? value) => value!.Trim();

    private static string Hash<T>(T value)
    {
        var json = JsonSerializer.Serialize(value, WebJson);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))
            .ToLowerInvariant();
    }

    private static ProductIdentityRejectedException InvalidAllocation() => Rejected(
        "IDENTITY_ALLOCATION_INVALID",
        "身份登记字段不完整或来源类型无效；每个产品必须有且仅有一个可证明来源的 SN。",
        422);

    private static ProductIdentityRejectedException IdentityNotFound() => Rejected(
        "PRODUCT_IDENTITY_NOT_FOUND",
        "未找到该产品身份；请核对扫描值或先由授权来源登记身份。",
        404);

    private static ProductIdentityRejectedException Rejected(
        string code,
        string message,
        int statusCode) => new(code, message, statusCode);

    private sealed record ProposedAllocation(
        string SourceSystem,
        string IdempotencyKey,
        string MaterialCode,
        IReadOnlyList<ProposedIdentifier> Identifiers,
        string CommandHash);

    private sealed record ProposedIdentifier(
        ControlledIdentifierType Type,
        string Value,
        IdentitySourceType SourceType,
        string SourceSystem,
        string SourceReference,
        bool IsDemo);
}
