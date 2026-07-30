using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Mes.Infrastructure.Persistence.Migrations;

[DbContext(typeof(MesDbContext))]
[Migration(MesMigrationIds.PreserveRejectedIngressGaps)]
/// <summary>允许拒绝消息保留缺失字段，并标记旧摘要算法，避免验证失败反而丢失原始接口证据。</summary>
public sealed class PreserveRejectedIngressGaps : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "BusinessKey",
            schema: "integration",
            table: "IntegrationInboxMessages",
            type: "nvarchar(160)",
            maxLength: 160,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "nvarchar(160)",
            oldMaxLength: 160);
        migrationBuilder.AlterColumn<string>(
            name: "SourceVersion",
            schema: "integration",
            table: "IntegrationInboxMessages",
            type: "nvarchar(80)",
            maxLength: 80,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "nvarchar(80)",
            oldMaxLength: 80);
        migrationBuilder.AlterColumn<string>(
            name: "ContractVersion",
            schema: "integration",
            table: "IntegrationInboxMessages",
            type: "nvarchar(32)",
            maxLength: 32,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "nvarchar(32)",
            oldMaxLength: 32);
        // 旧成功消息没有原始 JSON，不能冒充完整载荷摘要；显式标记旧算法以保持证据含义诚实。
        migrationBuilder.Sql("""
            UPDATE [integration].[IntegrationInboxMessages]
            SET [PayloadHashAlgorithm] = N'SHA-256-DTO-V1'
            WHERE [PayloadJson] IS NULL
              AND [PayloadHashAlgorithm] = N'SHA-256';

            UPDATE [integration].[IntegrationInboxMessages]
            SET [PayloadHashAlgorithm] = N'SHA-256-RAW-V1'
            WHERE [PayloadJson] IS NOT NULL
              AND [PayloadHashAlgorithm] = N'SHA-256';
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // 只有不存在字段缺口时才允许降级，防止把无法表示的拒绝证据静默截断。
        migrationBuilder.Sql("""
            IF EXISTS (
                SELECT 1
                FROM [integration].[IntegrationInboxMessages]
                WHERE [BusinessKey] IS NULL
                   OR [SourceVersion] IS NULL
                   OR [ContractVersion] IS NULL)
                THROW 51006, 'Cannot downgrade while rejected Inbox evidence contains missing fields.', 1;

            UPDATE [integration].[IntegrationInboxMessages]
            SET [PayloadHashAlgorithm] = N'SHA-256'
            WHERE [PayloadHashAlgorithm] IN (N'SHA-256-DTO-V1', N'SHA-256-RAW-V1');
            """);
        migrationBuilder.AlterColumn<string>(
            name: "BusinessKey",
            schema: "integration",
            table: "IntegrationInboxMessages",
            type: "nvarchar(160)",
            maxLength: 160,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(160)",
            oldMaxLength: 160,
            oldNullable: true);
        migrationBuilder.AlterColumn<string>(
            name: "SourceVersion",
            schema: "integration",
            table: "IntegrationInboxMessages",
            type: "nvarchar(80)",
            maxLength: 80,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(80)",
            oldMaxLength: 80,
            oldNullable: true);
        migrationBuilder.AlterColumn<string>(
            name: "ContractVersion",
            schema: "integration",
            table: "IntegrationInboxMessages",
            type: "nvarchar(32)",
            maxLength: 32,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(32)",
            oldMaxLength: 32,
            oldNullable: true);
    }
}
