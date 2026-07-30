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

/// <summary>
/// 管理版本化测试规范的起草、批准和读取。
/// 已批准规范不可覆盖，模板只能引用明确版本，从而支持质量职责分离和历史复现。
/// </summary>
public sealed class TestSpecificationService(
    MesDbContext context,
    IdentityAccessService identityAccess,
    TimeProvider timeProvider)
{
    private const string HashAlgorithm = "SHA-256-JSON-V1";
    private const decimal MaximumStoredNumeric = 999999999999.999999m;
    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);
    private readonly BusinessAuditWriter auditWriter = new(context, timeProvider);

    public async Task<TestSpecificationVersionView> CreateDraftAsync(
        EffectiveIdentity actor,
        TestSpecificationDraftRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        const string action = "TEST_SPECIFICATION_DRAFT_CREATE";
        await identityAccess.DemandCapabilityAsync(
            actor,
            BusinessCapability.ProcessDefinitionManage,
            action,
            "TestSpecificationVersion",
            $"{request.Code}:{request.Version}",
            correlationId,
            cancellationToken);
        var parsed = ParseDraft(request);
        // 版本唯一性和草稿落库在 Serializable 事务内完成，防止两个并发请求创建同一版本。
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            var material = await context.Materials.SingleOrDefaultAsync(
                item => item.Code == parsed.MaterialCode && item.IsActive,
                cancellationToken) ?? throw Rejected(
                "TEST_SPECIFICATION_MATERIAL_NOT_FOUND",
                "适用成品物料不存在或未启用，请先核对 ERP/MES 主数据。",
                422);
            if (await context.TestSpecificationVersions.AnyAsync(
                    item => item.Code == parsed.Code && item.Version == parsed.Version,
                    cancellationToken))
            {
                throw Rejected(
                    "TEST_SPECIFICATION_VERSION_EXISTS",
                    "该测试规范版本已经存在，请创建新的版本号，不得覆盖原版本。",
                    409);
            }

            var now = timeProvider.GetUtcNow();
            var entity = new TestSpecificationVersion
            {
                Id = Guid.NewGuid(),
                Code = parsed.Code,
                Version = parsed.Version,
                MaterialId = material.Id,
                OperationCode = parsed.OperationCode,
                Applicability = parsed.Applicability,
                DefinitionJson = parsed.DefinitionJson,
                DefinitionHash = parsed.DefinitionHash,
                DefinitionHashAlgorithm = HashAlgorithm,
                CreatedAtUtc = now,
                CreatedByUserId = actor.UserId,
            };
            context.TestSpecificationVersions.Add(entity);
            AppendAudit(
                actor,
                BusinessRole.ProcessEngineer,
                BusinessCapability.ProcessDefinitionManage,
                action,
                $"{entity.Code}:{entity.Version}",
                correlationId);
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return ToView(entity, material.Code, parsed.Items, correlationId);
        });
    }

    public async Task<TestSpecificationVersionView> ApproveAsync(
        EffectiveIdentity actor,
        string code,
        string version,
        TestSpecificationApprovalRequest request,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        const string action = "TEST_SPECIFICATION_APPROVE";
        var normalizedCode = Required(code, 80, "TEST_SPECIFICATION_INVALID");
        var normalizedVersion = Required(version, 80, "TEST_SPECIFICATION_INVALID");
        var evidence = Required(
            request.ApprovalEvidenceReference,
            400,
            "TEST_SPECIFICATION_APPROVAL_EVIDENCE_REQUIRED");
        await identityAccess.DemandCapabilityAsync(
            actor,
            BusinessCapability.TestSpecificationApprove,
            action,
            "TestSpecificationVersion",
            $"{normalizedCode}:{normalizedVersion}",
            correlationId,
            cancellationToken);
        var strategy = context.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            context.ChangeTracker.Clear();
            await using var transaction = await context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);
            var entity = await context.TestSpecificationVersions.SingleOrDefaultAsync(
                item => item.Code == normalizedCode && item.Version == normalizedVersion,
                cancellationToken) ?? throw Rejected(
                "TEST_SPECIFICATION_NOT_FOUND",
                "未找到待批准的测试规范版本，请核对规范编码和版本。",
                404);
            var materialCode = await context.Materials
                .Where(item => item.Id == entity.MaterialId)
                .Select(item => item.Code)
                .SingleAsync(cancellationToken);
            if (!entity.IsApproved)
            {
                entity.IsApproved = true;
                entity.ApprovedAtUtc = timeProvider.GetUtcNow();
                entity.ApprovedByUserId = actor.UserId;
                entity.ApprovedByUsername = actor.Username;
                entity.ApprovalEvidenceReference = evidence;
                AppendAudit(
                    actor,
                    BusinessRole.QualityEngineer,
                    BusinessCapability.TestSpecificationApprove,
                    action,
                    $"{entity.Code}:{entity.Version}",
                    correlationId);
                await context.SaveChangesAsync(cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return ToView(entity, materialCode, DeserializeItems(entity.DefinitionJson), correlationId);
        });
    }

    public async Task<TestSpecificationVersionView> ReadAsync(
        EffectiveIdentity actor,
        string code,
        string version,
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = Required(code, 80, "TEST_SPECIFICATION_INVALID");
        var normalizedVersion = Required(version, 80, "TEST_SPECIFICATION_INVALID");
        await identityAccess.DemandCapabilityAsync(
            actor,
            BusinessCapability.TestSpecificationRead,
            "TEST_SPECIFICATION_READ",
            "TestSpecificationVersion",
            $"{normalizedCode}:{normalizedVersion}",
            correlationId,
            cancellationToken);
        var entity = await context.TestSpecificationVersions
            .AsNoTracking()
            .SingleOrDefaultAsync(
                item => item.Code == normalizedCode && item.Version == normalizedVersion,
                cancellationToken) ?? throw Rejected(
                "TEST_SPECIFICATION_NOT_FOUND",
                "未找到测试规范版本，请核对规范编码和版本。",
                404);
        var materialCode = await context.Materials
            .Where(item => item.Id == entity.MaterialId)
            .Select(item => item.Code)
            .SingleAsync(cancellationToken);
        return ToView(entity, materialCode, DeserializeItems(entity.DefinitionJson), correlationId);
    }

    private static ParsedDraft ParseDraft(TestSpecificationDraftRequest request)
    {
        var code = Required(request.Code, 80, "TEST_SPECIFICATION_INVALID");
        var version = Required(request.Version, 80, "TEST_SPECIFICATION_INVALID");
        var materialCode = Required(request.MaterialCode, 80, "TEST_SPECIFICATION_INVALID");
        var operationCode = Required(request.OperationCode, 80, "TEST_SPECIFICATION_INVALID");
        var applicability = Required(request.Applicability, 400, "TEST_SPECIFICATION_INVALID");
        if (request.Items is not { Count: > 0 })
        {
            throw Rejected(
                "TEST_SPECIFICATION_INVALID",
                "测试规范必须包含至少一个定义完整的测试项目。",
                422);
        }

        var items = request.Items.Select(ParseItem).OrderBy(item => item.Code, StringComparer.Ordinal).ToArray();
        if (items.Select(item => item.Code).Distinct(StringComparer.Ordinal).Count() != items.Length)
        {
            throw Rejected(
                "TEST_SPECIFICATION_ITEM_DUPLICATE",
                "测试项目编码不得重复，请核对规范定义。",
                422);
        }

        var json = JsonSerializer.Serialize(items, WebJson);
        var canonicalDefinition = JsonSerializer.Serialize(
            new
            {
                code,
                version,
                materialCode,
                operationCode,
                applicability,
                items,
            },
            WebJson);
        var hash = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(canonicalDefinition)));
        return new ParsedDraft(code, version, materialCode, operationCode, applicability, items, json, hash);
    }

    private static TestSpecificationItemDefinition ParseItem(TestSpecificationItemDefinition item)
    {
        var code = Required(item.Code, 80, "TEST_SPECIFICATION_ITEM_INVALID");
        var name = Required(item.Name, 160, "TEST_SPECIFICATION_ITEM_INVALID");
        var dataType = Required(item.DataType, 16, "TEST_SPECIFICATION_ITEM_INVALID");
        var unit = Normalize(item.Unit);
        var expectedText = Normalize(item.ExpectedText);
        var numeric = string.Equals(dataType, "Numeric", StringComparison.Ordinal);
        var text = string.Equals(dataType, "Text", StringComparison.Ordinal);
        var boolean = string.Equals(dataType, "Boolean", StringComparison.Ordinal);
        var valid = numeric
            ? unit.Length is > 0 and <= 24
                && item.DecimalPlaces is >= 0 and <= 6
                && (item.LowerLimit is not null || item.UpperLimit is not null)
                && (item.LowerLimit is null || item.UpperLimit is null || item.LowerLimit <= item.UpperLimit)
                && IsStorable(item.LowerLimit)
                && IsStorable(item.UpperLimit)
                && expectedText.Length == 0
                && item.ExpectedBoolean is null
            : text
                ? unit.Length == 0
                    && expectedText.Length is > 0 and <= 400
                    && item.DecimalPlaces is null
                    && item.LowerLimit is null
                    && item.UpperLimit is null
                    && item.ExpectedBoolean is null
                : boolean
                    && unit.Length == 0
                    && expectedText.Length == 0
                    && item.DecimalPlaces is null
                    && item.LowerLimit is null
                    && item.UpperLimit is null
                    && item.ExpectedBoolean is not null;
        if (!valid)
        {
            throw Rejected(
                "TEST_SPECIFICATION_ITEM_INVALID",
                $"测试项目 {code} 的数据类型、单位或判定规则不完整。",
                422);
        }

        return new TestSpecificationItemDefinition(
            code,
            name,
            dataType,
            item.Required,
            unit.Length == 0 ? null : unit,
            item.DecimalPlaces,
            item.LowerLimit,
            item.UpperLimit,
            expectedText.Length == 0 ? null : expectedText,
            item.ExpectedBoolean);
    }

    private static TestSpecificationVersionView ToView(
        TestSpecificationVersion entity,
        string materialCode,
        IReadOnlyList<TestSpecificationItemDefinition> items,
        string correlationId) => new(
            entity.Id,
            entity.Code,
            entity.Version,
            materialCode,
            entity.OperationCode,
            entity.Applicability,
            entity.IsApproved ? "Approved" : "Draft",
            entity.DefinitionHash,
            items,
            entity.CreatedAtUtc,
            entity.ApprovedAtUtc,
            entity.ApprovedByUsername,
            entity.ApprovalEvidenceReference,
            correlationId);

    private void AppendAudit(
        EffectiveIdentity actor,
        BusinessRole role,
        BusinessCapability capability,
        string action,
        string objectId,
        string correlationId) => auditWriter.Append(new BusinessAuditWrite(
            BusinessAuditActor.From(actor),
            role,
            capability,
            action,
            "TestSpecificationVersion",
            objectId,
            BusinessAuditResult.Succeeded,
            null,
            correlationId));

    private static TestSpecificationItemDefinition[] DeserializeItems(string json) =>
        JsonSerializer.Deserialize<TestSpecificationItemDefinition[]>(json, WebJson)
        ?? throw Rejected(
            "TEST_SPECIFICATION_DEFINITION_INVALID",
            "测试规范定义无法解析，请停止使用该版本并联系工艺与质量人员。",
            500);

    private static string Required(string? value, int maximumLength, string code)
    {
        var normalized = Normalize(value);
        return normalized.Length is > 0 && normalized.Length <= maximumLength
            ? normalized
            : throw Rejected(code, "测试规范请求字段不完整或超过允许长度。", 422);
    }

    private static string Normalize(string? value) => value?.Trim() ?? string.Empty;

    private static bool IsStorable(decimal? value) =>
        value is null || value is >= -MaximumStoredNumeric and <= MaximumStoredNumeric;

    private static TestSpecificationRejectedException Rejected(
        string code,
        string message,
        int statusCode) => new(code, message, statusCode);

    private sealed record ParsedDraft(
        string Code,
        string Version,
        string MaterialCode,
        string OperationCode,
        string Applicability,
        IReadOnlyList<TestSpecificationItemDefinition> Items,
        string DefinitionJson,
        string DefinitionHash);
}
