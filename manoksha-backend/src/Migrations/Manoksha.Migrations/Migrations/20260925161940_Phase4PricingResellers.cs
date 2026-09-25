using System;
using Manoksha.Persistence.Platform;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Manoksha.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Phase4PricingResellers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "resellers");

            migrationBuilder.EnsureSchema(
                name: "pricing");

            migrationBuilder.EnsureSchema(
                name: "wallet");

            migrationBuilder.CreateSequence(
                name: "reseller_number_seq",
                schema: "resellers");

            migrationBuilder.CreateTable(
                name: "product_reseller_discounts",
                schema: "pricing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    discount_pct = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    set_by = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    end_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_reseller_discounts", x => x.id);
                    table.CheckConstraint("ck_product_discount_range", "discount_pct >= 0 AND discount_pct <= 100");
                });

            migrationBuilder.CreateTable(
                name: "resellers",
                schema: "resellers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reseller_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    business_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    address_line = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    state = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    pin = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    mobile_e164 = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_resellers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "retail_prices",
                schema: "pricing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    price = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    effective_to = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    set_by = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_retail_prices", x => x.id);
                    table.CheckConstraint("ck_retail_prices_positive", "price > 0");
                });

            migrationBuilder.CreateTable(
                name: "wallets",
                schema: "wallet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reseller_id = table.Column<Guid>(type: "uuid", nullable: false),
                    balance = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_wallets", x => x.id);
                    table.CheckConstraint("ck_wallets_balance_non_negative", "balance >= 0");
                });

            migrationBuilder.CreateTable(
                name: "commercial_terms",
                schema: "resellers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reseller_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    discount_pct = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    effective_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_commercial_terms", x => x.id);
                    table.ForeignKey(
                        name: "fk_commercial_terms_resellers_reseller_id",
                        column: x => x.reseller_id,
                        principalSchema: "resellers",
                        principalTable: "resellers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reseller_status_changes",
                schema: "resellers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reseller_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    to_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reseller_status_changes", x => x.id);
                    table.ForeignKey(
                        name: "fk_reseller_status_changes_resellers_reseller_id",
                        column: x => x.reseller_id,
                        principalSchema: "resellers",
                        principalTable: "resellers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_commercial_terms_reseller_id_version",
                schema: "resellers",
                table: "commercial_terms",
                columns: new[] { "reseller_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_reseller_discounts_product_id_effective_from",
                schema: "pricing",
                table: "product_reseller_discounts",
                columns: new[] { "product_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ux_product_reseller_discounts_current",
                schema: "pricing",
                table: "product_reseller_discounts",
                column: "product_id",
                unique: true,
                filter: "effective_to IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_reseller_status_changes_reseller_id_occurred_at",
                schema: "resellers",
                table: "reseller_status_changes",
                columns: new[] { "reseller_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_resellers_mobile_e164",
                schema: "resellers",
                table: "resellers",
                column: "mobile_e164",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_resellers_reseller_number",
                schema: "resellers",
                table: "resellers",
                column: "reseller_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_resellers_status",
                schema: "resellers",
                table: "resellers",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_resellers_user_id",
                schema: "resellers",
                table: "resellers",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_retail_prices_sku_id_effective_from",
                schema: "pricing",
                table: "retail_prices",
                columns: new[] { "sku_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ux_retail_prices_current",
                schema: "pricing",
                table: "retail_prices",
                column: "sku_id",
                unique: true,
                filter: "effective_to IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_wallets_reseller_id",
                schema: "wallet",
                table: "wallets",
                column: "reseller_id",
                unique: true);

            // Cross-module FKs are checked at commit: EF does not know these relationships, so it cannot order inserts for them.
            migrationBuilder.Sql("ALTER TABLE identity.user_role_assignments ALTER CONSTRAINT fk_user_role_assignments_branch DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE employees.employees ALTER CONSTRAINT fk_employees_branch DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE employees.employees ALTER CONSTRAINT fk_employees_user DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE employees.employee_branch_assignments ALTER CONSTRAINT fk_employee_branch_assignments_branch DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE employees.attendance_records ALTER CONSTRAINT fk_attendance_records_branch DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE inventory.stock_levels ALTER CONSTRAINT fk_stock_levels_sku DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE inventory.stock_levels ALTER CONSTRAINT fk_stock_levels_branch DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE inventory.inventory_items ALTER CONSTRAINT fk_inventory_items_sku DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE inventory.inventory_items ALTER CONSTRAINT fk_inventory_items_branch DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE inventory.cost_layers ALTER CONSTRAINT fk_cost_layers_sku DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE inventory.cost_layers ALTER CONSTRAINT fk_cost_layers_branch DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE inventory.transfers ALTER CONSTRAINT fk_transfers_source DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE inventory.transfers ALTER CONSTRAINT fk_transfers_destination DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE inventory.transfer_lines ALTER CONSTRAINT fk_transfer_lines_sku DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE inventory.adjustments ALTER CONSTRAINT fk_adjustments_sku DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE inventory.adjustments ALTER CONSTRAINT fk_adjustments_branch DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE inventory.discrepancies ALTER CONSTRAINT fk_discrepancies_branch DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE inventory.stock_counts ALTER CONSTRAINT fk_stock_counts_branch DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE purchasing.purchase_orders ALTER CONSTRAINT fk_purchase_orders_branch DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE purchasing.purchase_order_lines ALTER CONSTRAINT fk_po_lines_sku DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE purchasing.goods_receipts ALTER CONSTRAINT fk_goods_receipts_branch DEFERRABLE INITIALLY DEFERRED;");

            // Cross-module integrity.
            migrationBuilder.Sql("ALTER TABLE resellers.resellers ADD CONSTRAINT fk_resellers_user FOREIGN KEY (user_id) REFERENCES identity.users (id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE wallet.wallets ADD CONSTRAINT fk_wallets_reseller FOREIGN KEY (reseller_id) REFERENCES resellers.resellers (id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE pricing.retail_prices ADD CONSTRAINT fk_retail_prices_sku FOREIGN KEY (sku_id) REFERENCES catalog.skus (id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE pricing.product_reseller_discounts ADD CONSTRAINT fk_product_reseller_discounts_product FOREIGN KEY (product_id) REFERENCES catalog.products (id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;");

            // Immutable history (SPEC §15 versioned terms, §30 audit).
            migrationBuilder.Sql(AppendOnly.Protect("resellers", "reseller_status_changes"));
            migrationBuilder.Sql(AppendOnly.Protect("resellers", "commercial_terms"));
            migrationBuilder.Sql(AppendOnly.CreateCloseOnlyFunctionSql);
            migrationBuilder.Sql(AppendOnly.ProtectCloseOnly("pricing", "retail_prices"));
            migrationBuilder.Sql(AppendOnly.ProtectCloseOnly("pricing", "product_reseller_discounts"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE identity.user_role_assignments ALTER CONSTRAINT fk_user_role_assignments_branch NOT DEFERRABLE;");
            migrationBuilder.Sql("ALTER TABLE employees.employees ALTER CONSTRAINT fk_employees_branch NOT DEFERRABLE;");
            migrationBuilder.Sql("ALTER TABLE employees.employees ALTER CONSTRAINT fk_employees_user NOT DEFERRABLE;");
            migrationBuilder.Sql("ALTER TABLE employees.employee_branch_assignments ALTER CONSTRAINT fk_employee_branch_assignments_branch NOT DEFERRABLE;");
            migrationBuilder.Sql("ALTER TABLE employees.attendance_records ALTER CONSTRAINT fk_attendance_records_branch NOT DEFERRABLE;");
            migrationBuilder.Sql("ALTER TABLE inventory.stock_levels ALTER CONSTRAINT fk_stock_levels_sku NOT DEFERRABLE;");
            migrationBuilder.Sql("ALTER TABLE inventory.stock_levels ALTER CONSTRAINT fk_stock_levels_branch NOT DEFERRABLE;");
            migrationBuilder.Sql("ALTER TABLE inventory.inventory_items ALTER CONSTRAINT fk_inventory_items_sku NOT DEFERRABLE;");
            migrationBuilder.Sql("ALTER TABLE inventory.inventory_items ALTER CONSTRAINT fk_inventory_items_branch NOT DEFERRABLE;");
            migrationBuilder.Sql("ALTER TABLE inventory.cost_layers ALTER CONSTRAINT fk_cost_layers_sku NOT DEFERRABLE;");
            migrationBuilder.Sql("ALTER TABLE inventory.cost_layers ALTER CONSTRAINT fk_cost_layers_branch NOT DEFERRABLE;");
            migrationBuilder.Sql("ALTER TABLE inventory.transfers ALTER CONSTRAINT fk_transfers_source NOT DEFERRABLE;");
            migrationBuilder.Sql("ALTER TABLE inventory.transfers ALTER CONSTRAINT fk_transfers_destination NOT DEFERRABLE;");
            migrationBuilder.Sql("ALTER TABLE inventory.transfer_lines ALTER CONSTRAINT fk_transfer_lines_sku NOT DEFERRABLE;");
            migrationBuilder.Sql("ALTER TABLE inventory.adjustments ALTER CONSTRAINT fk_adjustments_sku NOT DEFERRABLE;");
            migrationBuilder.Sql("ALTER TABLE inventory.adjustments ALTER CONSTRAINT fk_adjustments_branch NOT DEFERRABLE;");
            migrationBuilder.Sql("ALTER TABLE inventory.discrepancies ALTER CONSTRAINT fk_discrepancies_branch NOT DEFERRABLE;");
            migrationBuilder.Sql("ALTER TABLE inventory.stock_counts ALTER CONSTRAINT fk_stock_counts_branch NOT DEFERRABLE;");
            migrationBuilder.Sql("ALTER TABLE purchasing.purchase_orders ALTER CONSTRAINT fk_purchase_orders_branch NOT DEFERRABLE;");
            migrationBuilder.Sql("ALTER TABLE purchasing.purchase_order_lines ALTER CONSTRAINT fk_po_lines_sku NOT DEFERRABLE;");
            migrationBuilder.Sql("ALTER TABLE purchasing.goods_receipts ALTER CONSTRAINT fk_goods_receipts_branch NOT DEFERRABLE;");

            migrationBuilder.Sql(AppendOnly.UnprotectCloseOnly("pricing", "product_reseller_discounts"));
            migrationBuilder.Sql(AppendOnly.UnprotectCloseOnly("pricing", "retail_prices"));
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS platform.allow_only_closing_history();");
            migrationBuilder.Sql(AppendOnly.Unprotect("resellers", "commercial_terms"));
            migrationBuilder.Sql(AppendOnly.Unprotect("resellers", "reseller_status_changes"));
            migrationBuilder.Sql("ALTER TABLE pricing.product_reseller_discounts DROP CONSTRAINT IF EXISTS fk_product_reseller_discounts_product;");
            migrationBuilder.Sql("ALTER TABLE pricing.retail_prices DROP CONSTRAINT IF EXISTS fk_retail_prices_sku;");
            migrationBuilder.Sql("ALTER TABLE wallet.wallets DROP CONSTRAINT IF EXISTS fk_wallets_reseller;");
            migrationBuilder.Sql("ALTER TABLE resellers.resellers DROP CONSTRAINT IF EXISTS fk_resellers_user;");

            migrationBuilder.DropTable(
                name: "commercial_terms",
                schema: "resellers");

            migrationBuilder.DropTable(
                name: "product_reseller_discounts",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "reseller_status_changes",
                schema: "resellers");

            migrationBuilder.DropTable(
                name: "retail_prices",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "wallets",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "resellers",
                schema: "resellers");

            migrationBuilder.DropSequence(
                name: "reseller_number_seq",
                schema: "resellers");
        }
    }
}
