using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mes.Domain.Auditing;
using Mes.Domain.Execution;
using Mes.Domain.Identity;
using Mes.Domain.MasterData;
using Mes.Infrastructure.IdentityAccess;
using Mes.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Mes.Infrastructure.Execution;

public sealed class ExecutionTemplateService(
    MesDbContext context,
    IdentityAccessService identityAccess,
    TimeProvider timeProvider)
{
    private const string PublishAction = "EXECUTION_TEMPLATE_PUBLISH";
    private readonly BusinessAuditWriter auditWriter = new(context, timeProvider);

    public async Task<ExecutionTemplatePublishResult> PublishAsync(
        EffectiveIdentity actor,
        ExecutionTemplatePublishRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        await identityAccess.DemandCapabilityAsync(
            actor,
            BusinessCapability.ProcessDefinitionManage,
            PublishAction,
            "ProductExecutionTemplateVersion",
            request.MaterialCode,
            correlationId,
            cancellationToken);
        await identityAccess.DemandCapabilityAsync(
            actor,
            BusinessCapability.TestSpecificationApprove,
            PublishAction,
            "ProductExecutionTemplateVersion",
            request.MaterialCode,
            correlationId,
            cancellationToken);

        var validationError = Validate(request);
        if (validationError is not null)
        {
            await DenyAsync(actor, request.MaterialCode, validationError.Code, correlationId, cancellationToken);
            throw validationError;
        }

        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            var material = await context.Materials.SingleOrDefaultAsync(
                item => item.Code == request.MaterialCode && item.IsActive,
                cancellationToken);
            if (material is null)
            {
                await DenyAsync(actor, request.MaterialCode, "TEMPLATE_PRODUCT_NOT_FOUND", correlationId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                throw new ExecutionTemplateRejectedException(
                    "TEMPLATE_PRODUCT_NOT_FOUND",
                    "成品物料不存在或未启用；请先完成 ERP/MES 主数据准备。",
                    422);
            }

            var componentCodes = request.Bom.Components
                .Select(component => component.MaterialCode)
                .Distinct(StringComparer.Ordinal)
                .ToArray();
            var components = await context.Materials
                .Where(item => componentCodes.Contains(item.Code) && item.IsActive)
                .ToDictionaryAsync(item => item.Code, StringComparer.Ordinal, cancellationToken);
            var missingComponent = componentCodes.FirstOrDefault(code => !components.ContainsKey(code));
            if (missingComponent is not null)
            {
                await DenyAsync(actor, request.MaterialCode, "TEMPLATE_COMPONENT_NOT_FOUND", correlationId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                throw new ExecutionTemplateRejectedException(
                    "TEMPLATE_COMPONENT_NOT_FOUND",
                    $"BOM 组件 {missingComponent} 不存在或未启用；请先完成主数据准备。",
                    422);
            }

            var traceabilityConflict = request.Bom.Components.FirstOrDefault(component =>
                !string.Equals(
                    components[component.MaterialCode].TraceabilityMode.ToString(),
                    component.TraceabilityMode,
                    StringComparison.Ordinal));
            if (traceabilityConflict is not null
                || !string.Equals(
                    material.TraceabilityMode.ToString(),
                    request.TraceabilityPolicy.FinishedProductMode,
                    StringComparison.Ordinal))
            {
                await DenyAsync(actor, request.MaterialCode, "TEMPLATE_TRACEABILITY_CONFLICT", correlationId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                throw new ExecutionTemplateRejectedException(
                    "TEMPLATE_TRACEABILITY_CONFLICT",
                    "模板追溯粒度与已启用物料主数据不一致；请由工艺工程师核对后发布新版本。",
                    422);
            }

            if (await context.ProductExecutionTemplateVersions.AnyAsync(
                    template => template.MaterialId == material.Id
                        && template.Version == request.Version,
                    cancellationToken))
            {
                await DenyAsync(actor, request.MaterialCode, "TEMPLATE_VERSION_EXISTS", correlationId, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                throw new ExecutionTemplateRejectedException(
                    "TEMPLATE_VERSION_EXISTS",
                    "该成品的执行模板版本已存在；请发布新的版本号，不得覆盖原版本。",
                    409);
            }

            var definition = new ExecutionTemplateDefinition(
                new ProductDefinition(
                    material.Code,
                    material.Name,
                    request.ProductVersion,
                    material.TraceabilityMode.ToString()),
                request.Applicability,
                new BomDefinition(
                    request.Bom.Version,
                    request.Bom.Components
                        .OrderBy(component => component.MaterialCode, StringComparer.Ordinal)
                        .ToArray()),
                new RouteDefinition(
                    request.Route.Version,
                    request.Route.Operations
                        .OrderBy(operation => operation.Sequence)
                        .ToArray()),
                request.TraceabilityPolicy,
                request.IdentityPolicy,
                request.FirmwareRequirements,
                request.TestSpecifications,
                request.CompletionGate);
            var definitionJson = JsonSerializer.Serialize(definition, JsonSerializerOptions.Web);
            var definitionHash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(definitionJson)));
            var template = new ProductExecutionTemplateVersion
            {
                Id = Guid.NewGuid(),
                MaterialId = material.Id,
                Version = request.Version,
                Applicability = request.Applicability,
                DefinitionJson = definitionJson,
                DefinitionHash = definitionHash,
                DefinitionHashAlgorithm = "SHA-256-JSON-V1",
                IsApproved = true,
                PublishedAtUtc = timeProvider.GetUtcNow(),
                PublishedByUserId = actor.UserId,
            };
            context.ProductExecutionTemplateVersions.Add(template);
            auditWriter.Append(new BusinessAuditWrite(
                BusinessAuditActor.From(actor),
                BusinessRole.ProcessEngineer,
                BusinessCapability.ProcessDefinitionManage,
                PublishAction,
                "ProductExecutionTemplateVersion",
                $"{request.MaterialCode}:{request.Version}",
                BusinessAuditResult.Succeeded,
                null,
                correlationId));
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new ExecutionTemplatePublishResult(
                template.Id,
                material.Code,
                template.Version,
                "Approved",
                template.DefinitionHash);
        });
    }

    private static ExecutionTemplateRejectedException? Validate(
        ExecutionTemplatePublishRequest request)
    {
        var requiredTextMissing = string.IsNullOrWhiteSpace(request.MaterialCode)
            || string.IsNullOrWhiteSpace(request.ProductVersion)
            || string.IsNullOrWhiteSpace(request.Version)
            || string.IsNullOrWhiteSpace(request.Applicability)
            || string.IsNullOrWhiteSpace(request.Bom?.Version)
            || string.IsNullOrWhiteSpace(request.Route?.Version)
            || string.IsNullOrWhiteSpace(request.TraceabilityPolicy?.Version)
            || string.IsNullOrWhiteSpace(request.IdentityPolicy?.Version)
            || string.IsNullOrWhiteSpace(request.IdentityPolicy?.FinishedSerialSource)
            || string.IsNullOrWhiteSpace(request.IdentityPolicy?.AllocationTiming)
            || string.IsNullOrWhiteSpace(request.CompletionGate?.Version);
        var collectionMissing = request.Bom?.Components is not { Count: > 0 }
            || request.Route?.Operations is not { Count: > 0 }
            || request.IdentityPolicy?.RequiredIdentifiers is not { Count: > 0 }
            || request.FirmwareRequirements is not { Count: > 0 }
            || request.TestSpecifications is not { Count: > 0 }
            || request.CompletionGate?.Requirements is not { Count: > 0 };
        if (requiredTextMissing || collectionMissing)
        {
            return InvalidTemplate();
        }

        var bom = request.Bom!;
        var route = request.Route!;
        var operationCodes = route.Operations
            .Select(operation => operation.Code)
            .ToHashSet(StringComparer.Ordinal);
        if (bom.Components.Any(component =>
                string.IsNullOrWhiteSpace(component.MaterialCode)
                || string.IsNullOrWhiteSpace(component.Unit)
                || string.IsNullOrWhiteSpace(component.AssemblyOperationCode)
                || component.QuantityPer <= 0
                || !Enum.TryParse<TraceabilityMode>(component.TraceabilityMode, out var mode)
                || !operationCodes.Contains(component.AssemblyOperationCode)
                || !IsConsumptionRuleSupported(mode, component.ConsumptionRule)
                || (mode == TraceabilityMode.Serial
                    && component.QuantityPer != decimal.Truncate(component.QuantityPer)))
            || bom.Components
                .Select(component => new
                {
                    component.MaterialCode,
                    component.AssemblyOperationCode,
                })
                .Distinct()
                .Count() != bom.Components.Count
            || route.Operations.Any(operation =>
                operation.Sequence <= 0
                || string.IsNullOrWhiteSpace(operation.Code)
                || string.IsNullOrWhiteSpace(operation.Name))
            || route.Operations.Select(operation => operation.Sequence).Distinct().Count()
                != route.Operations.Count
            || route.Operations.Select(operation => operation.Code).Distinct(StringComparer.Ordinal).Count()
                != route.Operations.Count
            || request.IdentityPolicy!.RequiredIdentifiers.Any(string.IsNullOrWhiteSpace)
            || request.IdentityPolicy.RequiredIdentifiers.Distinct(StringComparer.Ordinal).Count()
                != request.IdentityPolicy.RequiredIdentifiers.Count
            || !SupportedSerialSources.Contains(request.IdentityPolicy.FinishedSerialSource)
            || !SupportedAllocationTimings.Contains(request.IdentityPolicy.AllocationTiming)
            || request.FirmwareRequirements.Any(requirement =>
                string.IsNullOrWhiteSpace(requirement.Code)
                || string.IsNullOrWhiteSpace(requirement.Version)
                || string.IsNullOrWhiteSpace(requirement.EvidenceReference)
                || string.IsNullOrWhiteSpace(requirement.OperationCode)
                || string.IsNullOrWhiteSpace(requirement.ConfigurationPackage)
                || string.IsNullOrWhiteSpace(requirement.ChecksumAlgorithm)
                || string.IsNullOrWhiteSpace(requirement.ExpectedChecksum)
                || !operationCodes.Contains(requirement.OperationCode))
            || request.FirmwareRequirements.Select(requirement => requirement.Code)
                .Distinct(StringComparer.Ordinal).Count() != request.FirmwareRequirements.Count
            || request.TestSpecifications.Any(specification =>
                string.IsNullOrWhiteSpace(specification.Code)
                || string.IsNullOrWhiteSpace(specification.Version)
                || string.IsNullOrWhiteSpace(specification.EvidenceReference))
            || request.CompletionGate!.Requirements.Any(string.IsNullOrWhiteSpace)
            || request.CompletionGate.Requirements.Distinct(StringComparer.Ordinal).Count()
                != request.CompletionGate.Requirements.Count)
        {
            return InvalidTemplate();
        }

        return null;
    }

    private static bool IsConsumptionRuleSupported(
        TraceabilityMode traceabilityMode,
        string? consumptionRule) => traceabilityMode switch
        {
            TraceabilityMode.Serial or TraceabilityMode.Lot =>
                string.Equals(consumptionRule, "PerProductActual", StringComparison.Ordinal),
            TraceabilityMode.None =>
                string.Equals(consumptionRule, "PerProductActual", StringComparison.Ordinal)
                || string.Equals(consumptionRule, "OrderBackflush", StringComparison.Ordinal),
            _ => false,
        };

    private static readonly HashSet<string> SupportedSerialSources =
    [
        "ERP",
        "LabelSystem",
        "MesControlledPool",
        "DemoControlledPool",
        "AuthorizedExternalOrControlledPool",
    ];

    private static readonly HashSet<string> SupportedAllocationTimings =
    [
        "AtOrderRelease",
        "BeforeStartWip",
        "OnDemandBeforeStartWip",
    ];

    private static ExecutionTemplateRejectedException InvalidTemplate() => new(
        "EXECUTION_TEMPLATE_INVALID",
        "执行模板必须完整包含版本化 BOM、路线、追溯、身份、固件、测试和完成门禁，且数量与工序顺序有效。",
        422);

    private async Task DenyAsync(
        EffectiveIdentity actor,
        string objectId,
        string reasonCode,
        string correlationId,
        CancellationToken cancellationToken)
    {
        auditWriter.Append(new BusinessAuditWrite(
            BusinessAuditActor.From(actor),
            BusinessRole.ProcessEngineer,
            BusinessCapability.ProcessDefinitionManage,
            PublishAction,
            "ProductExecutionTemplateVersion",
            string.IsNullOrWhiteSpace(objectId) ? correlationId : objectId,
            BusinessAuditResult.Denied,
            reasonCode,
            correlationId));
        await context.SaveChangesAsync(cancellationToken);
    }
}
