using System;
using Manoksha.Persistence.Platform;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Manoksha.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Phase3PurchasingInventory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "inventory");

            migrationBuilder.EnsureSchema(
                name: "purchasing");

            migrationBuilder.CreateSequence(
                name: "adjustment_seq",
                schema: "inventory");

            migrationBuilder.CreateSequence(
                name: "count_seq",
                schema: "inventory");

            migrationBuilder.CreateSequence(
                name: "discrepancy_seq",
                schema: "inventory");

            migrationBuilder.CreateSequence(
                name: "grn_seq",
                schema: "purchasing");

            migrationBuilder.CreateSequence(
                name: "po_seq",
                schema: "purchasing");

            migrationBuilder.CreateSequence(
                name: "supplier_seq",
                schema: "purchasing");

            migrationBuilder.CreateSequence(
                name: "transfer_seq",
                schema: "inventory");

            migrationBuilder.CreateTable(
                name: "cost_layers",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    seq = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    layer_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    original_qty = table.Column<int>(type: "integer", nullable: false),
                    remaining_qty = table.Column<int>(type: "integer", nullable: false),
                    source_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    origin_layer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cost_layers", x => x.id);
                    table.CheckConstraint("ck_cost_layers_remaining", "remaining_qty >= 0 AND remaining_qty <= original_qty");
                    table.CheckConstraint("ck_cost_layers_unit_cost", "unit_cost >= 0");
                });

            migrationBuilder.CreateTable(
                name: "discrepancies",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    expected_qty = table.Column<int>(type: "integer", nullable: false),
                    actual_qty = table.Column<int>(type: "integer", nullable: false),
                    missing_item_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    resolution = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    resolution_notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    resolved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_discrepancies", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inventory_items",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    barcode = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    source_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    source_id = table.Column<Guid>(type: "uuid", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    written_off_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_items", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "inventory_movements",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    seq = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    from_branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    to_branch_id = table.Column<Guid>(type: "uuid", nullable: true),
                    from_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    to_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    movement_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    reference_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_inventory_movements", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock_counts",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    submitted_by = table.Column<Guid>(type: "uuid", nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_counts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stock_levels",
                schema: "inventory",
                columns: table => new
                {
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_levels", x => new { x.sku_id, x.branch_id, x.status });
                    table.CheckConstraint("ck_stock_levels_quantity_non_negative", "quantity >= 0");
                });

            migrationBuilder.CreateTable(
                name: "suppliers",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    contact_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    mobile_e164 = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    gstin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    address = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_suppliers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "transfers",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    source_branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    destination_branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    approved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    approved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    owner_self_authorized = table.Column<bool>(type: "boolean", nullable: false),
                    decision_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    prepared_by = table.Column<Guid>(type: "uuid", nullable: true),
                    prepared_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    dispatched_by = table.Column<Guid>(type: "uuid", nullable: true),
                    dispatched_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    received_by = table.Column<Guid>(type: "uuid", nullable: true),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_by = table.Column<Guid>(type: "uuid", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transfers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "cost_layer_consumptions",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    layer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reference_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_cost_layer_consumptions", x => x.id);
                    table.ForeignKey(
                        name: "fk_cost_layer_consumptions_cost_layers_layer_id",
                        column: x => x.layer_id,
                        principalSchema: "inventory",
                        principalTable: "cost_layers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "adjustments",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    from_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    to_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    item_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    reason_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    discrepancy_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                    requested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    decided_by = table.Column<Guid>(type: "uuid", nullable: true),
                    decided_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    unit_cost = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    value_at_cost = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    required_owner = table.Column<bool>(type: "boolean", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_adjustments", x => x.id);
                    table.ForeignKey(
                        name: "fk_adjustments_discrepancies_discrepancy_id",
                        column: x => x.discrepancy_id,
                        principalSchema: "inventory",
                        principalTable: "discrepancies",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "stock_count_lines",
                schema: "inventory",
                columns: table => new
                {
                    count_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    counted_qty = table.Column<int>(type: "integer", nullable: true),
                    system_qty = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_stock_count_lines", x => new { x.count_id, x.sku_id });
                    table.ForeignKey(
                        name: "fk_stock_count_lines_stock_counts_count_id",
                        column: x => x.count_id,
                        principalSchema: "inventory",
                        principalTable: "stock_counts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_orders",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    receiving_branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    supplier_reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    expected_date = table.Column<DateOnly>(type: "date", nullable: true),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    issued_by = table.Column<Guid>(type: "uuid", nullable: true),
                    issued_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    closed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    close_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_orders", x => x.id);
                    table.ForeignKey(
                        name: "fk_purchase_orders_suppliers_supplier_id",
                        column: x => x.supplier_id,
                        principalSchema: "purchasing",
                        principalTable: "suppliers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "transfer_lines",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    transfer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    serialized = table.Column<bool>(type: "boolean", nullable: false),
                    requested_qty = table.Column<int>(type: "integer", nullable: false),
                    prepared_qty = table.Column<int>(type: "integer", nullable: false),
                    dispatched_qty = table.Column<int>(type: "integer", nullable: false),
                    received_qty = table.Column<int>(type: "integer", nullable: false),
                    resolved_qty = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transfer_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_transfer_lines_transfers_transfer_id",
                        column: x => x.transfer_id,
                        principalSchema: "inventory",
                        principalTable: "transfers",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "goods_receipts",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    purchase_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    supplier_invoice_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    received_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goods_receipts", x => x.id);
                    table.ForeignKey(
                        name: "fk_goods_receipts_purchase_orders_purchase_order_id",
                        column: x => x.purchase_order_id,
                        principalSchema: "purchasing",
                        principalTable: "purchase_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "purchase_order_lines",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    ordered_qty = table.Column<int>(type: "integer", nullable: false),
                    expected_unit_cost = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    received_qty = table.Column<int>(type: "integer", nullable: false),
                    damaged_qty = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_purchase_order_lines", x => x.id);
                    table.CheckConstraint("ck_po_lines_received", "received_qty >= 0 AND received_qty <= ordered_qty AND damaged_qty >= 0 AND damaged_qty <= received_qty");
                    table.ForeignKey(
                        name: "fk_purchase_order_lines_purchase_orders_purchase_order_id",
                        column: x => x.purchase_order_id,
                        principalSchema: "purchasing",
                        principalTable: "purchase_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "transfer_cost_allocations",
                schema: "inventory",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    transfer_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    source_layer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    layer_date = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    layer_seq = table.Column<long>(type: "bigint", nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    settled_qty = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transfer_cost_allocations", x => x.id);
                    table.CheckConstraint("ck_transfer_alloc_settled", "settled_qty >= 0 AND settled_qty <= quantity");
                    table.ForeignKey(
                        name: "fk_transfer_cost_allocations_transfer_lines_transfer_line_id",
                        column: x => x.transfer_line_id,
                        principalSchema: "inventory",
                        principalTable: "transfer_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "transfer_line_items",
                schema: "inventory",
                columns: table => new
                {
                    transfer_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    item_id = table.Column<Guid>(type: "uuid", nullable: false),
                    outcome = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_transfer_line_items", x => new { x.transfer_line_id, x.item_id });
                    table.ForeignKey(
                        name: "fk_transfer_line_items_inventory_items_item_id",
                        column: x => x.item_id,
                        principalSchema: "inventory",
                        principalTable: "inventory_items",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_transfer_line_items_transfer_lines_transfer_line_id",
                        column: x => x.transfer_line_id,
                        principalSchema: "inventory",
                        principalTable: "transfer_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "goods_receipt_lines",
                schema: "purchasing",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    goods_receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purchase_order_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    received_qty = table.Column<int>(type: "integer", nullable: false),
                    damaged_qty = table.Column<int>(type: "integer", nullable: false),
                    unit_cost = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_goods_receipt_lines", x => x.id);
                    table.ForeignKey(
                        name: "fk_goods_receipt_lines_goods_receipts_goods_receipt_id",
                        column: x => x.goods_receipt_id,
                        principalSchema: "purchasing",
                        principalTable: "goods_receipts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_goods_receipt_lines_purchase_order_lines_purchase_order_lin",
                        column: x => x.purchase_order_line_id,
                        principalSchema: "purchasing",
                        principalTable: "purchase_order_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_adjustments_branch_id_status",
                schema: "inventory",
                table: "adjustments",
                columns: new[] { "branch_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_adjustments_discrepancy_id",
                schema: "inventory",
                table: "adjustments",
                column: "discrepancy_id");

            migrationBuilder.CreateIndex(
                name: "ix_adjustments_number",
                schema: "inventory",
                table: "adjustments",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_cost_layer_consumptions_layer_id",
                schema: "inventory",
                table: "cost_layer_consumptions",
                column: "layer_id");

            migrationBuilder.CreateIndex(
                name: "ix_cost_layer_consumptions_reference_type_reference_id",
                schema: "inventory",
                table: "cost_layer_consumptions",
                columns: new[] { "reference_type", "reference_id" });

            migrationBuilder.CreateIndex(
                name: "ix_cost_layers_fifo",
                schema: "inventory",
                table: "cost_layers",
                columns: new[] { "sku_id", "branch_id", "layer_date", "seq" },
                filter: "remaining_qty > 0");

            migrationBuilder.CreateIndex(
                name: "ix_discrepancies_branch_id_status",
                schema: "inventory",
                table: "discrepancies",
                columns: new[] { "branch_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_discrepancies_number",
                schema: "inventory",
                table: "discrepancies",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_discrepancies_source_type_source_id",
                schema: "inventory",
                table: "discrepancies",
                columns: new[] { "source_type", "source_id" });

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_lines_goods_receipt_id",
                schema: "purchasing",
                table: "goods_receipt_lines",
                column: "goods_receipt_id");

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipt_lines_purchase_order_line_id",
                schema: "purchasing",
                table: "goods_receipt_lines",
                column: "purchase_order_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipts_branch_id_received_at",
                schema: "purchasing",
                table: "goods_receipts",
                columns: new[] { "branch_id", "received_at" });

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipts_number",
                schema: "purchasing",
                table: "goods_receipts",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_goods_receipts_purchase_order_id",
                schema: "purchasing",
                table: "goods_receipts",
                column: "purchase_order_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_items_available",
                schema: "inventory",
                table: "inventory_items",
                columns: new[] { "sku_id", "branch_id" },
                filter: "status = 'Available'");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_items_barcode",
                schema: "inventory",
                table: "inventory_items",
                column: "barcode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_inventory_items_sku_id_branch_id_status",
                schema: "inventory",
                table: "inventory_items",
                columns: new[] { "sku_id", "branch_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movements_item_id",
                schema: "inventory",
                table: "inventory_movements",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movements_reference_type_reference_id",
                schema: "inventory",
                table: "inventory_movements",
                columns: new[] { "reference_type", "reference_id" });

            migrationBuilder.CreateIndex(
                name: "ix_inventory_movements_sku_id_seq",
                schema: "inventory",
                table: "inventory_movements",
                columns: new[] { "sku_id", "seq" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_order_lines_purchase_order_id_sku_id",
                schema: "purchasing",
                table: "purchase_order_lines",
                columns: new[] { "purchase_order_id", "sku_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_number",
                schema: "purchasing",
                table: "purchase_orders",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_receiving_branch_id_status",
                schema: "purchasing",
                table: "purchase_orders",
                columns: new[] { "receiving_branch_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_purchase_orders_supplier_id",
                schema: "purchasing",
                table: "purchase_orders",
                column: "supplier_id");

            migrationBuilder.CreateIndex(
                name: "ix_stock_counts_branch_id_status",
                schema: "inventory",
                table: "stock_counts",
                columns: new[] { "branch_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_stock_counts_number",
                schema: "inventory",
                table: "stock_counts",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stock_levels_branch_id_status",
                schema: "inventory",
                table: "stock_levels",
                columns: new[] { "branch_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_suppliers_code",
                schema: "purchasing",
                table: "suppliers",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_suppliers_name",
                schema: "purchasing",
                table: "suppliers",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "ix_transfer_cost_allocations_transfer_line_id",
                schema: "inventory",
                table: "transfer_cost_allocations",
                column: "transfer_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_transfer_line_items_item_id",
                schema: "inventory",
                table: "transfer_line_items",
                column: "item_id");

            migrationBuilder.CreateIndex(
                name: "ix_transfer_lines_transfer_id_sku_id",
                schema: "inventory",
                table: "transfer_lines",
                columns: new[] { "transfer_id", "sku_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transfers_destination_branch_id_status",
                schema: "inventory",
                table: "transfers",
                columns: new[] { "destination_branch_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_transfers_number",
                schema: "inventory",
                table: "transfers",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_transfers_source_branch_id_status",
                schema: "inventory",
                table: "transfers",
                columns: new[] { "source_branch_id", "status" });
            // Cross-module integrity.
            migrationBuilder.Sql("ALTER TABLE inventory.stock_levels ADD CONSTRAINT fk_stock_levels_sku FOREIGN KEY (sku_id) REFERENCES catalog.skus (id) ON DELETE RESTRICT;");
            migrationBuilder.Sql("ALTER TABLE inventory.stock_levels ADD CONSTRAINT fk_stock_levels_branch FOREIGN KEY (branch_id) REFERENCES branches.branches (id) ON DELETE RESTRICT;");
            migrationBuilder.Sql("ALTER TABLE inventory.inventory_items ADD CONSTRAINT fk_inventory_items_sku FOREIGN KEY (sku_id) REFERENCES catalog.skus (id) ON DELETE RESTRICT;");
            migrationBuilder.Sql("ALTER TABLE inventory.inventory_items ADD CONSTRAINT fk_inventory_items_branch FOREIGN KEY (branch_id) REFERENCES branches.branches (id) ON DELETE RESTRICT;");
            migrationBuilder.Sql("ALTER TABLE inventory.cost_layers ADD CONSTRAINT fk_cost_layers_sku FOREIGN KEY (sku_id) REFERENCES catalog.skus (id) ON DELETE RESTRICT;");
            migrationBuilder.Sql("ALTER TABLE inventory.cost_layers ADD CONSTRAINT fk_cost_layers_branch FOREIGN KEY (branch_id) REFERENCES branches.branches (id) ON DELETE RESTRICT;");
            migrationBuilder.Sql("ALTER TABLE inventory.transfers ADD CONSTRAINT fk_transfers_source FOREIGN KEY (source_branch_id) REFERENCES branches.branches (id) ON DELETE RESTRICT;");
            migrationBuilder.Sql("ALTER TABLE inventory.transfers ADD CONSTRAINT fk_transfers_destination FOREIGN KEY (destination_branch_id) REFERENCES branches.branches (id) ON DELETE RESTRICT;");
            migrationBuilder.Sql("ALTER TABLE inventory.transfer_lines ADD CONSTRAINT fk_transfer_lines_sku FOREIGN KEY (sku_id) REFERENCES catalog.skus (id) ON DELETE RESTRICT;");
            migrationBuilder.Sql("ALTER TABLE inventory.adjustments ADD CONSTRAINT fk_adjustments_sku FOREIGN KEY (sku_id) REFERENCES catalog.skus (id) ON DELETE RESTRICT;");
            migrationBuilder.Sql("ALTER TABLE inventory.adjustments ADD CONSTRAINT fk_adjustments_branch FOREIGN KEY (branch_id) REFERENCES branches.branches (id) ON DELETE RESTRICT;");
            migrationBuilder.Sql("ALTER TABLE inventory.discrepancies ADD CONSTRAINT fk_discrepancies_branch FOREIGN KEY (branch_id) REFERENCES branches.branches (id) ON DELETE RESTRICT;");
            migrationBuilder.Sql("ALTER TABLE inventory.stock_counts ADD CONSTRAINT fk_stock_counts_branch FOREIGN KEY (branch_id) REFERENCES branches.branches (id) ON DELETE RESTRICT;");
            migrationBuilder.Sql("ALTER TABLE catalog.barcodes ADD CONSTRAINT fk_barcodes_inventory_item FOREIGN KEY (inventory_item_id) REFERENCES inventory.inventory_items (id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE purchasing.purchase_orders ADD CONSTRAINT fk_purchase_orders_branch FOREIGN KEY (receiving_branch_id) REFERENCES branches.branches (id) ON DELETE RESTRICT;");
            migrationBuilder.Sql("ALTER TABLE purchasing.purchase_order_lines ADD CONSTRAINT fk_po_lines_sku FOREIGN KEY (sku_id) REFERENCES catalog.skus (id) ON DELETE RESTRICT;");
            migrationBuilder.Sql("ALTER TABLE purchasing.goods_receipts ADD CONSTRAINT fk_goods_receipts_branch FOREIGN KEY (branch_id) REFERENCES branches.branches (id) ON DELETE RESTRICT;");

            // Immutable history (SPEC §9, §30).
            migrationBuilder.Sql(AppendOnly.Protect("inventory", "inventory_movements"));
            migrationBuilder.Sql(AppendOnly.Protect("inventory", "cost_layer_consumptions"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(AppendOnly.Unprotect("inventory", "cost_layer_consumptions"));
            migrationBuilder.Sql(AppendOnly.Unprotect("inventory", "inventory_movements"));
            migrationBuilder.Sql("ALTER TABLE purchasing.goods_receipts DROP CONSTRAINT IF EXISTS fk_goods_receipts_branch;");
            migrationBuilder.Sql("ALTER TABLE purchasing.purchase_order_lines DROP CONSTRAINT IF EXISTS fk_po_lines_sku;");
            migrationBuilder.Sql("ALTER TABLE purchasing.purchase_orders DROP CONSTRAINT IF EXISTS fk_purchase_orders_branch;");
            migrationBuilder.Sql("ALTER TABLE catalog.barcodes DROP CONSTRAINT IF EXISTS fk_barcodes_inventory_item;");
            migrationBuilder.Sql("ALTER TABLE inventory.stock_counts DROP CONSTRAINT IF EXISTS fk_stock_counts_branch;");
            migrationBuilder.Sql("ALTER TABLE inventory.discrepancies DROP CONSTRAINT IF EXISTS fk_discrepancies_branch;");
            migrationBuilder.Sql("ALTER TABLE inventory.adjustments DROP CONSTRAINT IF EXISTS fk_adjustments_branch;");
            migrationBuilder.Sql("ALTER TABLE inventory.adjustments DROP CONSTRAINT IF EXISTS fk_adjustments_sku;");
            migrationBuilder.Sql("ALTER TABLE inventory.transfer_lines DROP CONSTRAINT IF EXISTS fk_transfer_lines_sku;");
            migrationBuilder.Sql("ALTER TABLE inventory.transfers DROP CONSTRAINT IF EXISTS fk_transfers_destination;");
            migrationBuilder.Sql("ALTER TABLE inventory.transfers DROP CONSTRAINT IF EXISTS fk_transfers_source;");
            migrationBuilder.Sql("ALTER TABLE inventory.cost_layers DROP CONSTRAINT IF EXISTS fk_cost_layers_branch;");
            migrationBuilder.Sql("ALTER TABLE inventory.cost_layers DROP CONSTRAINT IF EXISTS fk_cost_layers_sku;");
            migrationBuilder.Sql("ALTER TABLE inventory.inventory_items DROP CONSTRAINT IF EXISTS fk_inventory_items_branch;");
            migrationBuilder.Sql("ALTER TABLE inventory.inventory_items DROP CONSTRAINT IF EXISTS fk_inventory_items_sku;");
            migrationBuilder.Sql("ALTER TABLE inventory.stock_levels DROP CONSTRAINT IF EXISTS fk_stock_levels_branch;");
            migrationBuilder.Sql("ALTER TABLE inventory.stock_levels DROP CONSTRAINT IF EXISTS fk_stock_levels_sku;");

            migrationBuilder.DropTable(
                name: "adjustments",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "cost_layer_consumptions",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "goods_receipt_lines",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "inventory_movements",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_count_lines",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "stock_levels",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "transfer_cost_allocations",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "transfer_line_items",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "discrepancies",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "cost_layers",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "goods_receipts",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "purchase_order_lines",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "stock_counts",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "inventory_items",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "transfer_lines",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "purchase_orders",
                schema: "purchasing");

            migrationBuilder.DropTable(
                name: "transfers",
                schema: "inventory");

            migrationBuilder.DropTable(
                name: "suppliers",
                schema: "purchasing");

            migrationBuilder.DropSequence(
                name: "adjustment_seq",
                schema: "inventory");

            migrationBuilder.DropSequence(
                name: "count_seq",
                schema: "inventory");

            migrationBuilder.DropSequence(
                name: "discrepancy_seq",
                schema: "inventory");

            migrationBuilder.DropSequence(
                name: "grn_seq",
                schema: "purchasing");

            migrationBuilder.DropSequence(
                name: "po_seq",
                schema: "purchasing");

            migrationBuilder.DropSequence(
                name: "supplier_seq",
                schema: "purchasing");

            migrationBuilder.DropSequence(
                name: "transfer_seq",
                schema: "inventory");
        }
    }
}
