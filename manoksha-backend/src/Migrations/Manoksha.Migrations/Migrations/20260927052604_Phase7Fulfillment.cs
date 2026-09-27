using System;
using Manoksha.Persistence.Platform;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Manoksha.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Phase7Fulfillment : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "fulfillment_exceptions",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    raised_by = table.Column<Guid>(type: "uuid", nullable: false),
                    raised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    resolved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    resolution = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fulfillment_exceptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_fulfillment_exceptions_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "orders",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_reroutes",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    exception_id = table.Column<Guid>(type: "uuid", nullable: true),
                    inventory_effects = table.Column<string>(type: "jsonb", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_reroutes", x => x.id);
                    table.ForeignKey(
                        name: "fk_order_reroutes_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "orders",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "shipments",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    courier = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    courier_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    tracking_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    shipped_by = table.Column<Guid>(type: "uuid", nullable: false),
                    shipped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    delivered_on = table.Column<DateOnly>(type: "date", nullable: true),
                    delivered_recorded_by = table.Column<Guid>(type: "uuid", nullable: true),
                    delivered_recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivery_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_shipments", x => x.id);
                    table.ForeignKey(
                        name: "fk_shipments_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "orders",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fulfillment_exception_lines",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    exception_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    missing_qty = table.Column<int>(type: "integer", nullable: false),
                    damaged_qty = table.Column<int>(type: "integer", nullable: false),
                    missing_item_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    damaged_item_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fulfillment_exception_lines", x => x.id);
                    table.CheckConstraint("ck_fulfillment_exception_lines_qty", "missing_qty >= 0 AND damaged_qty >= 0");
                    table.ForeignKey(
                        name: "fk_fulfillment_exception_lines_fulfillment_exceptions_exceptio",
                        column: x => x.exception_id,
                        principalSchema: "orders",
                        principalTable: "fulfillment_exceptions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_fulfillment_exception_lines_exception_id",
                schema: "orders",
                table: "fulfillment_exception_lines",
                column: "exception_id");

            migrationBuilder.CreateIndex(
                name: "ix_fulfillment_exceptions_status_raised_at",
                schema: "orders",
                table: "fulfillment_exceptions",
                columns: new[] { "status", "raised_at" });

            migrationBuilder.CreateIndex(
                name: "ux_fulfillment_exceptions_one_open_per_order",
                schema: "orders",
                table: "fulfillment_exceptions",
                column: "order_id",
                unique: true,
                filter: "status = 'Open'");

            migrationBuilder.CreateIndex(
                name: "ix_order_reroutes_order_id",
                schema: "orders",
                table: "order_reroutes",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_shipments_order_id",
                schema: "orders",
                table: "shipments",
                column: "order_id",
                unique: true);

            // Cross-module references are checked at commit.
            migrationBuilder.Sql("ALTER TABLE orders.shipments ADD CONSTRAINT fk_shipments_branch FOREIGN KEY (branch_id) REFERENCES branches.branches (id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE orders.fulfillment_exceptions ADD CONSTRAINT fk_fulfillment_exceptions_branch FOREIGN KEY (branch_id) REFERENCES branches.branches (id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE orders.fulfillment_exception_lines ADD CONSTRAINT fk_fulfillment_exception_lines_sku FOREIGN KEY (sku_id) REFERENCES catalog.skus (id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE orders.order_reroutes ADD CONSTRAINT fk_order_reroutes_from_branch FOREIGN KEY (from_branch_id) REFERENCES branches.branches (id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE orders.order_reroutes ADD CONSTRAINT fk_order_reroutes_to_branch FOREIGN KEY (to_branch_id) REFERENCES branches.branches (id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql(AppendOnly.Protect("orders", "order_reroutes"));
            migrationBuilder.Sql(AppendOnly.Protect("orders", "fulfillment_exception_lines"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(AppendOnly.Unprotect("orders", "fulfillment_exception_lines"));
            migrationBuilder.Sql(AppendOnly.Unprotect("orders", "order_reroutes"));
            migrationBuilder.Sql("ALTER TABLE orders.order_reroutes DROP CONSTRAINT IF EXISTS fk_order_reroutes_to_branch;");
            migrationBuilder.Sql("ALTER TABLE orders.order_reroutes DROP CONSTRAINT IF EXISTS fk_order_reroutes_from_branch;");
            migrationBuilder.Sql("ALTER TABLE orders.fulfillment_exception_lines DROP CONSTRAINT IF EXISTS fk_fulfillment_exception_lines_sku;");
            migrationBuilder.Sql("ALTER TABLE orders.fulfillment_exceptions DROP CONSTRAINT IF EXISTS fk_fulfillment_exceptions_branch;");
            migrationBuilder.Sql("ALTER TABLE orders.shipments DROP CONSTRAINT IF EXISTS fk_shipments_branch;");

            migrationBuilder.DropTable(
                name: "fulfillment_exception_lines",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "order_reroutes",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "shipments",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "fulfillment_exceptions",
                schema: "orders");
        }
    }
}
