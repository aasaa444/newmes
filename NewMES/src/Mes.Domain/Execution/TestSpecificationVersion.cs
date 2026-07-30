namespace Mes.Domain.Execution;

// 版本化测试规范。只能从草稿受控批准一次，批准后的定义和证据由数据库阻止改写。
public sealed class TestSpecificationVersion
{
    public Guid Id { get; init; }

    public required string Code { get; init; }

    public required string Version { get; init; }

    public Guid MaterialId { get; init; }

    public required string OperationCode { get; init; }

    public required string Applicability { get; init; }

    public required string DefinitionJson { get; init; }

    public required string DefinitionHash { get; init; }

    public required string DefinitionHashAlgorithm { get; init; }

    public bool IsApproved { get; set; }

    public DateTimeOffset CreatedAtUtc { get; init; }

    public Guid CreatedByUserId { get; init; }

    public DateTimeOffset? ApprovedAtUtc { get; set; }

    public Guid? ApprovedByUserId { get; set; }

    public string? ApprovedByUsername { get; set; }

    public string? ApprovalEvidenceReference { get; set; }
}
