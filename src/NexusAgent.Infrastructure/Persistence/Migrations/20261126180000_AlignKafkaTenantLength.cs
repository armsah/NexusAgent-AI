using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace NexusAgent.Infrastructure.Persistence.Migrations;

[DbContext(typeof(NexusAgentDbContext))]
[Migration("20261126180000_AlignKafkaTenantLength")]
public sealed class AlignKafkaTenantLength : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "ALTER TABLE kafka_workflow_events ALTER COLUMN tenant_id TYPE varchar(256);");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "ALTER TABLE kafka_workflow_events ALTER COLUMN tenant_id TYPE varchar(200);");
    }
}