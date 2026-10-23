using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace NexusAgent.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddOutboxClaimState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "claim_token",
                table: "outbox_messages",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "lease_expires_at",
                table: "outbox_messages",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "claim_token",
                table: "outbox_messages");

            migrationBuilder.DropColumn(
                name: "lease_expires_at",
                table: "outbox_messages");
        }
    }
}
