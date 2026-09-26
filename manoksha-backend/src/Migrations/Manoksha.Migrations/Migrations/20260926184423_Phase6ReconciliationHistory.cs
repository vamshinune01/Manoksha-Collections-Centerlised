using System;
using Manoksha.Persistence.Platform;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Manoksha.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Phase6ReconciliationHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reconciliation_history",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reconciliation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    from_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    to_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    external_refund_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reconciliation_history", x => x.id);
                    table.ForeignKey(
                        name: "fk_reconciliation_history_reconciliations_reconciliation_id",
                        column: x => x.reconciliation_id,
                        principalSchema: "payments",
                        principalTable: "reconciliations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reconciliation_history_reconciliation_id_occurred_at",
                schema: "payments",
                table: "reconciliation_history",
                columns: new[] { "reconciliation_id", "occurred_at" });

            migrationBuilder.Sql("ALTER TABLE payments.reconciliation_history ADD CONSTRAINT fk_reconciliation_history_actor FOREIGN KEY (actor_user_id) REFERENCES identity.users (id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql(AppendOnly.Protect("payments", "reconciliation_history"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(AppendOnly.Unprotect("payments", "reconciliation_history"));
            migrationBuilder.Sql("ALTER TABLE payments.reconciliation_history DROP CONSTRAINT IF EXISTS fk_reconciliation_history_actor;");

            migrationBuilder.DropTable(
                name: "reconciliation_history",
                schema: "payments");
        }
    }
}
