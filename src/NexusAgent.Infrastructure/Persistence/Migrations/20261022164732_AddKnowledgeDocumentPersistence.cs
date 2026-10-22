using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKnowledgeDocumentPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "knowledge_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    classification = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    owner_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    allowed_principal_ids = table.Column<string[]>(type: "text[]", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_knowledge_documents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "document_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    classification = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    owner_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    allowed_principal_ids = table.Column<string[]>(type: "text[]", nullable: false),
                    is_deleted = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_versions", x => x.id);
                    table.ForeignKey(
                        name: "FK_document_versions_knowledge_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "knowledge_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_document_versions_document_id_version",
                table: "document_versions",
                columns: new[] { "document_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_document_versions_tenant_id",
                table: "document_versions",
                column: "tenant_id");

            migrationBuilder.CreateIndex(
                name: "IX_knowledge_documents_tenant_id_status",
                table: "knowledge_documents",
                columns: new[] { "tenant_id", "status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_versions");

            migrationBuilder.DropTable(
                name: "knowledge_documents");
        }
    }
}
