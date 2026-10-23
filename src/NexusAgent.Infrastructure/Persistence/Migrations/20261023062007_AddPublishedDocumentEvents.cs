using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPublishedDocumentEvents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "published_document_events",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_version = table.Column<int>(type: "integer", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_published_document_events", x => x.event_id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_published_document_events_document",
                table: "published_document_events",
                columns: new[] { "tenant_id", "document_id", "document_version" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "published_document_events");
        }
    }
}
