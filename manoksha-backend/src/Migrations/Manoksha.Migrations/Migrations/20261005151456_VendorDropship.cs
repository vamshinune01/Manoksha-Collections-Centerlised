using System;
using Manoksha.Persistence.Platform;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Manoksha.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class VendorDropship : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "out_of_stock",
                schema: "catalog",
                table: "skus",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "product_code",
                schema: "catalog",
                table: "products",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "vendor_id",
                schema: "catalog",
                table: "products",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "fulfillment_branch_id",
                schema: "orders",
                table: "orders",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "fulfillment_mode",
                schema: "orders",
                table: "orders",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Branch");

            migrationBuilder.AddColumn<Guid>(
                name: "parcel_id",
                schema: "orders",
                table: "order_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "product_code",
                schema: "orders",
                table: "order_lines",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "vendor_id",
                schema: "orders",
                table: "order_lines",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "commercial_term_vendor_discounts",
                schema: "resellers",
                columns: table => new
                {
                    term_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    discount_pct = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_commercial_term_vendor_discounts", x => new { x.term_id, x.vendor_id });
                    table.CheckConstraint("ck_term_vendor_discount_range", "discount_pct >= 0 AND discount_pct <= 100");
                    table.ForeignKey(
                        name: "fk_commercial_term_vendor_discounts_commercial_terms_term_id",
                        column: x => x.term_id,
                        principalSchema: "resellers",
                        principalTable: "commercial_terms",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_parcels",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_id = table.Column<Guid>(type: "uuid", nullable: false),
                    vendor_code = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    vendor_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    shipping_fee = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    vendor_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    courier = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    courier_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    tracking_number = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    shipped_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    delivered_on = table.Column<DateOnly>(type: "date", nullable: true),
                    note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_parcels", x => x.id);
                    table.CheckConstraint("ck_order_parcels_shipping", "shipping_fee >= 0");
                    table.ForeignKey(
                        name: "fk_order_parcels_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "orders",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_online_discounts",
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
                    table.PrimaryKey("pk_product_online_discounts", x => x.id);
                    table.CheckConstraint("ck_product_online_discount_range", "discount_pct >= 0 AND discount_pct <= 100");
                });

            migrationBuilder.CreateTable(
                name: "vendors",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    shipping_fee = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    owner_margin_pct = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    next_product_number = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_vendors", x => x.id);
                    table.CheckConstraint("ck_vendors_code", "code ~ '^[A-Z]{2,4}$'");
                    table.CheckConstraint("ck_vendors_owner_margin", "owner_margin_pct IS NULL OR (owner_margin_pct >= 0 AND owner_margin_pct < 100)");
                    table.CheckConstraint("ck_vendors_shipping_fee", "shipping_fee >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_products_product_code",
                schema: "catalog",
                table: "products",
                column: "product_code",
                unique: true,
                filter: "product_code IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_products_vendor_id",
                schema: "catalog",
                table: "products",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_lines_parcel_id",
                schema: "orders",
                table: "order_lines",
                column: "parcel_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_lines_vendor_id",
                schema: "orders",
                table: "order_lines",
                column: "vendor_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_parcels_order_id_vendor_id",
                schema: "orders",
                table: "order_parcels",
                columns: new[] { "order_id", "vendor_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_order_parcels_vendor_id_status",
                schema: "orders",
                table: "order_parcels",
                columns: new[] { "vendor_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_product_online_discounts_product_id_effective_from",
                schema: "pricing",
                table: "product_online_discounts",
                columns: new[] { "product_id", "effective_from" });

            migrationBuilder.CreateIndex(
                name: "ux_product_online_discounts_current",
                schema: "pricing",
                table: "product_online_discounts",
                column: "product_id",
                unique: true,
                filter: "effective_to IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_vendors_code",
                schema: "catalog",
                table: "vendors",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_vendors_name",
                schema: "catalog",
                table: "vendors",
                column: "name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_order_lines_order_parcels_parcel_id",
                schema: "orders",
                table: "order_lines",
                column: "parcel_id",
                principalSchema: "orders",
                principalTable: "order_parcels",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_products_vendor_vendor_id",
                schema: "catalog",
                table: "products",
                column: "vendor_id",
                principalSchema: "catalog",
                principalTable: "vendors",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Cross-module references are checked at commit.
            migrationBuilder.Sql("ALTER TABLE orders.order_lines ADD CONSTRAINT fk_order_lines_vendor FOREIGN KEY (vendor_id) REFERENCES catalog.vendors (id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE orders.order_parcels ADD CONSTRAINT fk_order_parcels_vendor FOREIGN KEY (vendor_id) REFERENCES catalog.vendors (id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE resellers.commercial_term_vendor_discounts ADD CONSTRAINT fk_term_vendor_discounts_vendor FOREIGN KEY (vendor_id) REFERENCES catalog.vendors (id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE pricing.product_online_discounts ADD CONSTRAINT fk_product_online_discounts_product FOREIGN KEY (product_id) REFERENCES catalog.products (id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE pricing.product_online_discounts ADD CONSTRAINT fk_product_online_discounts_set_by FOREIGN KEY (set_by) REFERENCES identity.users (id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;");
            // Commercial terms are immutable versions; their vendor percentages too (ADR-001 §45).
            migrationBuilder.Sql(AppendOnly.Protect("resellers", "commercial_term_vendor_discounts"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(AppendOnly.Unprotect("resellers", "commercial_term_vendor_discounts"));
            migrationBuilder.Sql("ALTER TABLE orders.order_lines DROP CONSTRAINT IF EXISTS fk_order_lines_vendor;");
            migrationBuilder.Sql("ALTER TABLE orders.order_parcels DROP CONSTRAINT IF EXISTS fk_order_parcels_vendor;");
            migrationBuilder.Sql("ALTER TABLE resellers.commercial_term_vendor_discounts DROP CONSTRAINT IF EXISTS fk_term_vendor_discounts_vendor;");
            migrationBuilder.Sql("ALTER TABLE pricing.product_online_discounts DROP CONSTRAINT IF EXISTS fk_product_online_discounts_product;");
            migrationBuilder.Sql("ALTER TABLE pricing.product_online_discounts DROP CONSTRAINT IF EXISTS fk_product_online_discounts_set_by;");

            migrationBuilder.DropForeignKey(
                name: "fk_order_lines_order_parcels_parcel_id",
                schema: "orders",
                table: "order_lines");

            migrationBuilder.DropForeignKey(
                name: "fk_products_vendor_vendor_id",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropTable(
                name: "commercial_term_vendor_discounts",
                schema: "resellers");

            migrationBuilder.DropTable(
                name: "order_parcels",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "product_online_discounts",
                schema: "pricing");

            migrationBuilder.DropTable(
                name: "vendors",
                schema: "catalog");

            migrationBuilder.DropIndex(
                name: "ix_products_product_code",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropIndex(
                name: "ix_products_vendor_id",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropIndex(
                name: "ix_order_lines_parcel_id",
                schema: "orders",
                table: "order_lines");

            migrationBuilder.DropIndex(
                name: "ix_order_lines_vendor_id",
                schema: "orders",
                table: "order_lines");

            migrationBuilder.DropColumn(
                name: "out_of_stock",
                schema: "catalog",
                table: "skus");

            migrationBuilder.DropColumn(
                name: "product_code",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropColumn(
                name: "vendor_id",
                schema: "catalog",
                table: "products");

            migrationBuilder.DropColumn(
                name: "fulfillment_mode",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "parcel_id",
                schema: "orders",
                table: "order_lines");

            migrationBuilder.DropColumn(
                name: "product_code",
                schema: "orders",
                table: "order_lines");

            migrationBuilder.DropColumn(
                name: "vendor_id",
                schema: "orders",
                table: "order_lines");

            migrationBuilder.AlterColumn<Guid>(
                name: "fulfillment_branch_id",
                schema: "orders",
                table: "orders",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
