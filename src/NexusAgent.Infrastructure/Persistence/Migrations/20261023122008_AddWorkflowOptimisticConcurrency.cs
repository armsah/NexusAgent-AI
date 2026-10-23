using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusAgent.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Records the PostgreSQL xmin concurrency mapping in the EF model snapshot.
    /// xmin is a PostgreSQL system column and must not be created or dropped.
    /// </summary>
    public partial class AddWorkflowOptimisticConcurrency : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}