using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mes.Domain.Auditing;
using Mes.Domain.Execution;
using Mes.Domain.Identity;
using Mes.Domain.Integration;
using Mes.Infrastructure.IdentityAccess;
using Mes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Mes.Infrastructure.Integration;

public sealed class ProductionOrderIngressService(
    MesDbContext context,
    IdentityAccessService identityAccess,
    TimeProvider timeProvider)
{
    private const string MessageType = "ProductionOrderUpsert";
    private const string LegacyDtoHashAlgorithm = "SHA-256-DTO-V1";
    private const string RawPayloadHashAlgorithm = "SHA-256-RAW-V1";
    private const string AcceptedCode = "PRODUCTION_ORDER_ACCEPTED";
    private const string AcceptedMessage = "生产订单已接收，可由计划员检查后下达。";
    private const string ConflictCode = "INBOUND_IDEMPOTENCY_CONFLICT";
    private const string ConflictMessage =
        "同一来源消息 ID 的载荷与首次请求不一致；请核对 ERP 重发内容并使用新的消息 ID 提交受控变更。";
    private const string UnsupportedContractCode = "CONTRACT_VERSION_UNSUPPORTED";
    private const string UnsupportedContractMessage =
        "当前仅支持生产订单入站契约 1.0；请由 ERP 集成负责人转换版本后重试。";
    private const string InvalidPayloadCode = "PRODUCTION_ORDER_PAYLOAD_INVALID";
    private const string InvalidPayloadMessage =
        "生产订单号、业务键、来源版本、物料编码必须填写，且计划数量必须大于 0。";
    private const string MaterialNotFoundCode = "MATERIAL_NOT_FOUND";
    private const string MaterialNotFoundMessage =
        "MES 中不存在或未启用该成品物料；请先由主数据负责人维护物料后重试。";
    private const string InvalidEnvelopeCode = "INBOUND_ENVELOPE_INVALID";
    private const string InvalidEnvelopeMessage =
        "来源系统和消息 ID 必须填写；请由 ERP 集成负责人修正消息信封后重试。";
    private const string ChangeRequiredCode = "PRODUCTION_ORDER_CHANGE_REQUIRED";
    private const string ChangeRequiredMessage =
        "该 ERP 生产订单已进入 MES；请由计划员按受控变更流程处理新版本，现有工单未被覆盖。";
    private readonly BusinessAuditWriter auditWriter = new(context, timeProvider);

    public async Task<ProductionOrderIngressResult> ReceiveAsync(
        EffectiveIdentity actor,
        ProductionOrderIngressRequest request,
        string payloadJson,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var businessObjectId = string.IsNullOrWhiteSpace(request.BusinessKey)
            ? request.MessageId
            : request.BusinessKey;
        await identityAccess.DemandCapabilityAsync(
            actor,
            BusinessCapability.ProductionOrderManage,
            "ERP_PRODUCTION_ORDER_INGRESS",
            "ProductionOrder",
            businessObjectId,
            correlationId,
            cancellationToken);

        if (string.IsNullOrWhiteSpace(request.SourceSystem)
            || string.IsNullOrWhiteSpace(request.MessageId))
        {
            auditWriter.Append(new BusinessAuditWrite(
                BusinessAuditActor.From(actor),
                BusinessRole.Planner,
                BusinessCapability.ProductionOrderManage,
                "ERP_PRODUCTION_ORDER_INGRESS",
                "IntegrationMessage",
                correlationId,
                BusinessAuditResult.Denied,
                InvalidEnvelopeCode,
                correlationId));
            await context.SaveChangesAsync(cancellationToken);
            return new ProductionOrderIngressResult(
                IntegrationInboxStatus.Rejected.ToString(),
                InvalidEnvelopeCode,
                InvalidEnvelopeMessage,
                null,
                400);
        }

        var payloadHash = ComputePayloadHash(payloadJson);
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            var existing = await context.IntegrationInboxMessages
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    message => message.SourceSystem == request.SourceSystem
                        && message.MessageId == request.MessageId,
                    cancellationToken);
            if (existing is not null)
            {
                var observedHash = UsesLegacyDtoHash(existing)
                    ? ComputePayloadHash(JsonSerializer.Serialize(request))
                    : payloadHash;
                if (!string.Equals(
                        existing.PayloadHash,
                        observedHash,
                        StringComparison.Ordinal))
                {
                    var conflictAt = timeProvider.GetUtcNow();
                    context.IntegrationInboxConflicts.Add(new IntegrationInboxConflict
                    {
                        Id = Guid.NewGuid(),
                        InboxMessageId = existing.Id,
                        ExistingPayloadHash = existing.PayloadHash,
                        ObservedPayloadHash = observedHash,
                        ResultCode = ConflictCode,
                        ResultMessage = ConflictMessage,
                        OccurredAtUtc = conflictAt,
                        CorrelationId = correlationId,
                    });
                    auditWriter.Append(new BusinessAuditWrite(
                        BusinessAuditActor.From(actor),
                        BusinessRole.Planner,
                        BusinessCapability.ProductionOrderManage,
                        "ERP_PRODUCTION_ORDER_INGRESS",
                        "ProductionOrder",
                        businessObjectId,
                        BusinessAuditResult.Denied,
                        ConflictCode,
                        correlationId));
                    await context.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    return new ProductionOrderIngressResult(
                        IntegrationInboxStatus.Rejected.ToString(),
                        ConflictCode,
                        ConflictMessage,
                        existing.ProductionOrderId,
                        409);
                }

                await transaction.CommitAsync(cancellationToken);
                return ToResult(existing);
            }

            var rejection = Validate(request);
            var material = rejection is null
                ? await context.Materials.SingleOrDefaultAsync(
                    item => item.Code == request.MaterialCode && item.IsActive,
                    cancellationToken)
                : null;
            if (rejection is null && material is null)
            {
                rejection = new Rejection(MaterialNotFoundCode, MaterialNotFoundMessage, 422);
            }

            if (rejection is not null)
            {
                var rejected = AppendRejectedInbox(
                    actor,
                    request,
                    payloadJson,
                    payloadHash,
                    rejection,
                    null,
                    correlationId);
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return ToResult(rejected);
            }

            var existingOrder = await context.ProductionOrders
                .AsNoTracking()
                .SingleOrDefaultAsync(
                    order => order.SourceSystem == request.SourceSystem
                        && order.SourceReference == request.BusinessKey,
                    cancellationToken);
            if (existingOrder is not null)
            {
                var rejected = AppendRejectedInbox(
                    actor,
                    request,
                    payloadJson,
                    payloadHash,
                    new Rejection(ChangeRequiredCode, ChangeRequiredMessage, 409),
                    existingOrder.Id,
                    correlationId);
                await context.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                return ToResult(rejected);
            }

            var now = timeProvider.GetUtcNow();
            var order = new ProductionOrder
            {
                Id = Guid.NewGuid(),
                OrderNumber = request.OrderNumber,
                MaterialId = material!.Id,
                PlannedQuantity = request.PlannedQuantity,
                Status = ProductionOrderStatus.Received,
                CreatedAtUtc = now,
                SourceSystem = request.SourceSystem,
                SourceReference = request.BusinessKey,
                SourceVersion = request.SourceVersion,
            };
            var inbox = CreateInbox(
                request,
                payloadJson,
                payloadHash,
                IntegrationInboxStatus.Accepted,
                AcceptedCode,
                AcceptedMessage,
                200,
                order.Id,
                order,
                now);
            context.ProductionOrders.Add(order);
            context.IntegrationInboxMessages.Add(inbox);
            auditWriter.Append(new BusinessAuditWrite(
                BusinessAuditActor.From(actor),
                BusinessRole.Planner,
                BusinessCapability.ProductionOrderManage,
                "ERP_PRODUCTION_ORDER_INGRESS",
                "ProductionOrder",
                businessObjectId,
                BusinessAuditResult.Succeeded,
                null,
                correlationId));
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToResult(inbox);
        });
    }

    private static string ComputePayloadHash(string payloadJson) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payloadJson)));

    private static bool UsesLegacyDtoHash(IntegrationInboxMessage message) =>
        string.Equals(
            message.PayloadHashAlgorithm,
            LegacyDtoHashAlgorithm,
            StringComparison.Ordinal)
        || (string.Equals(
                message.PayloadHashAlgorithm,
                "SHA-256",
                StringComparison.Ordinal)
            && message.PayloadJson is null);

    private static ProductionOrderIngressResult ToResult(IntegrationInboxMessage message) =>
        new(
            message.Status.ToString(),
            message.ResultCode,
            message.ResultMessage,
            message.ProductionOrderId,
            message.HttpStatusCode);

    private static Rejection? Validate(ProductionOrderIngressRequest request)
    {
        if (!string.Equals(request.ContractVersion, "1.0", StringComparison.Ordinal))
        {
            return new Rejection(UnsupportedContractCode, UnsupportedContractMessage, 422);
        }

        return string.IsNullOrWhiteSpace(request.OrderNumber)
            || string.IsNullOrWhiteSpace(request.BusinessKey)
            || string.IsNullOrWhiteSpace(request.SourceVersion)
            || string.IsNullOrWhiteSpace(request.MaterialCode)
            || request.PlannedQuantity <= 0
                ? new Rejection(InvalidPayloadCode, InvalidPayloadMessage, 422)
                : null;
    }

    private IntegrationInboxMessage AppendRejectedInbox(
        EffectiveIdentity actor,
        ProductionOrderIngressRequest request,
        string payloadJson,
        string payloadHash,
        Rejection rejection,
        Guid? productionOrderId,
        string correlationId)
    {
        var rejectedAt = timeProvider.GetUtcNow();
        var rejected = CreateInbox(
            request,
            payloadJson,
            payloadHash,
            IntegrationInboxStatus.Rejected,
            rejection.Code,
            rejection.Message,
            rejection.HttpStatusCode,
            productionOrderId,
            null,
            rejectedAt);
        context.IntegrationInboxMessages.Add(rejected);
        auditWriter.Append(new BusinessAuditWrite(
            BusinessAuditActor.From(actor),
            BusinessRole.Planner,
            BusinessCapability.ProductionOrderManage,
            "ERP_PRODUCTION_ORDER_INGRESS",
            "ProductionOrder",
            string.IsNullOrWhiteSpace(request.BusinessKey)
                ? request.MessageId
                : request.BusinessKey,
            BusinessAuditResult.Denied,
            rejection.Code,
            correlationId));
        return rejected;
    }

    private static IntegrationInboxMessage CreateInbox(
        ProductionOrderIngressRequest request,
        string payloadJson,
        string payloadHash,
        IntegrationInboxStatus status,
        string resultCode,
        string resultMessage,
        int httpStatusCode,
        Guid? productionOrderId,
        ProductionOrder? productionOrder,
        DateTimeOffset processedAt) => new()
        {
            Id = Guid.NewGuid(),
            SourceSystem = request.SourceSystem,
            MessageId = request.MessageId,
            MessageType = MessageType,
            BusinessKey = request.BusinessKey,
            SourceVersion = request.SourceVersion,
            ContractVersion = request.ContractVersion,
            PayloadHash = payloadHash,
            PayloadHashAlgorithm = RawPayloadHashAlgorithm,
            PayloadJson = payloadJson,
            Status = status,
            ResultCode = resultCode,
            ResultMessage = resultMessage,
            HttpStatusCode = httpStatusCode,
            ReceivedAtUtc = processedAt,
            ProcessedAtUtc = processedAt,
            ProductionOrderId = productionOrderId,
            ProductionOrder = productionOrder,
        };

    private sealed record Rejection(string Code, string Message, int HttpStatusCode);
}
