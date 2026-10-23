using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Pgvector;

#nullable disable

namespace NexusAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentVectorIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "document_vector_indexes",
                columns: table => new
                {
                    document_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    content_version = table.Column<int>(type: "integer", nullable: false),
                    embedding_model = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    embedding = table.Column<Vector>(type: "vector(1536)", nullable: false),
                    indexed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_document_vector_indexes", x => x.document_id);
                    table.ForeignKey(
                        name: "FK_document_vector_indexes_knowledge_documents_document_id",
                        column: x => x.document_id,
                        principalTable: "knowledge_documents",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_document_vector_indexes_tenant_id",
                table: "document_vector_indexes",
                column: "tenant_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "document_vector_indexes");
        }
    }
}
