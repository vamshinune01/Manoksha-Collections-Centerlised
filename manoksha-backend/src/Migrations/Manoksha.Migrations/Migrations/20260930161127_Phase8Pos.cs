using System;
using Manoksha.Persistence.Platform;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Manoksha.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Phase8Pos : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "approval_pin_failures",
                schema: "identity",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "approval_pin_hash",
                schema: "identity",
                table: "users",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "approval_pin_locked_until",
                schema: "identity",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "pos_payments",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    method = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pos_payments", x => x.id);
                    table.CheckConstraint("ck_pos_payments_amount", "amount > 0");
                    table.ForeignKey(
                        name: "fk_pos_payments_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "orders",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "pos_price_overrides",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    original_unit_price = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    final_unit_price = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    discount_per_unit = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    discount_pct = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    approval_level = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    seller_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    approver_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pos_price_overrides", x => x.id);
                    table.CheckConstraint("ck_pos_price_overrides_prices", "final_unit_price >= 0 AND final_unit_price < original_unit_price AND discount_per_unit = original_unit_price - final_unit_price");
                    table.ForeignKey(
                        name: "fk_pos_price_overrides_order_lines_order_line_id",
                        column: x => x.order_line_id,
                        principalSchema: "orders",
                        principalTable: "order_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_pos_price_overrides_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "orders",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pos_payments_order_id",
                schema: "orders",
                table: "pos_payments",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_pos_price_overrides_branch_id_occurred_at",
                schema: "orders",
                table: "pos_price_overrides",
                columns: new[] { "branch_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_pos_price_overrides_order_id",
                schema: "orders",
                table: "pos_price_overrides",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_pos_price_overrides_order_line_id",
                schema: "orders",
                table: "pos_price_overrides",
                column: "order_line_id");

            // Cross-module references are checked at commit.
            migrationBuilder.Sql("ALTER TABLE orders.pos_price_overrides ADD CONSTRAINT fk_pos_price_overrides_branch FOREIGN KEY (branch_id) REFERENCES branches.branches (id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE orders.pos_price_overrides ADD CONSTRAINT fk_pos_price_overrides_sku FOREIGN KEY (sku_id) REFERENCES catalog.skus (id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE orders.pos_price_overrides ADD CONSTRAINT fk_pos_price_overrides_seller FOREIGN KEY (seller_user_id) REFERENCES identity.users (id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE orders.pos_price_overrides ADD CONSTRAINT fk_pos_price_overrides_approver FOREIGN KEY (approver_user_id) REFERENCES identity.users (id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE orders.pos_payments ADD CONSTRAINT fk_pos_payments_recorded_by FOREIGN KEY (recorded_by) REFERENCES identity.users (id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql(AppendOnly.Protect("orders", "pos_price_overrides"));
            migrationBuilder.Sql(AppendOnly.Protect("orders", "pos_payments"));

            // Phase 8 Owner decision: sales staff may discount within their own limit (pos.staff_max_discount_pct).
            migrationBuilder.Sql("INSERT INTO identity.role_permissions (role_id, permission_code) SELECT r.id, 'pos.price_override' FROM identity.roles r " +
                "WHERE r.code = 'SALES_EMPLOYEE' AND EXISTS (SELECT 1 FROM identity.permissions WHERE code = 'pos.price_override') ON CONFLICT DO NOTHING;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(AppendOnly.Unprotect("orders", "pos_payments"));
            migrationBuilder.Sql(AppendOnly.Unprotect("orders", "pos_price_overrides"));
            migrationBuilder.Sql("ALTER TABLE orders.pos_payments DROP CONSTRAINT IF EXISTS fk_pos_payments_recorded_by;");
            migrationBuilder.Sql("ALTER TABLE orders.pos_price_overrides DROP CONSTRAINT IF EXISTS fk_pos_price_overrides_approver;");
            migrationBuilder.Sql("ALTER TABLE orders.pos_price_overrides DROP CONSTRAINT IF EXISTS fk_pos_price_overrides_seller;");
            migrationBuilder.Sql("ALTER TABLE orders.pos_price_overrides DROP CONSTRAINT IF EXISTS fk_pos_price_overrides_sku;");
            migrationBuilder.Sql("ALTER TABLE orders.pos_price_overrides DROP CONSTRAINT IF EXISTS fk_pos_price_overrides_branch;");

            migrationBuilder.DropTable(
                name: "pos_payments",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "pos_price_overrides",
                schema: "orders");

            migrationBuilder.DropColumn(
                name: "approval_pin_failures",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "approval_pin_hash",
                schema: "identity",
                table: "users");

            migrationBuilder.DropColumn(
                name: "approval_pin_locked_until",
                schema: "identity",
                table: "users");
        }
    }
}
