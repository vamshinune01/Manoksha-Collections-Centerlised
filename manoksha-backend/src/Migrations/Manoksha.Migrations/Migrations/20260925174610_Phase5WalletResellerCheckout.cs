using System;
using Manoksha.Persistence.Platform;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Manoksha.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Phase5WalletResellerCheckout : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "orders");

            migrationBuilder.CreateSequence(
                name: "deposit_seq",
                schema: "wallet");

            migrationBuilder.CreateSequence(
                name: "inquiry_number_seq",
                schema: "orders");

            migrationBuilder.CreateSequence(
                name: "order_number_seq",
                schema: "orders");

            migrationBuilder.CreateTable(
                name: "files",
                schema: "wallet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    object_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    size = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    uploaded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_files", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fulfillment_inquiries",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reseller_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    contact_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    contact_mobile = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    cart = table.Column<string>(type: "jsonb", nullable: false),
                    evaluations = table.Column<string>(type: "jsonb", nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fulfillment_inquiries", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "ledger_entries",
                schema: "wallet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    seq = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    wallet_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reseller_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    direction = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    balance_before = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    balance_after = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    order_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    deposit_request_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reverses_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_ledger_entries", x => x.id);
                    table.CheckConstraint("ck_ledger_amount_positive", "amount > 0");
                    table.CheckConstraint("ck_ledger_balance_after_non_negative", "balance_after >= 0");
                    table.CheckConstraint("ck_ledger_balance_arithmetic", "(direction = 'Credit' AND balance_after = balance_before + amount) OR (direction = 'Debit' AND balance_after = balance_before - amount)");
                    table.ForeignKey(
                        name: "fk_ledger_entries_wallets_wallet_id",
                        column: x => x.wallet_id,
                        principalSchema: "wallet",
                        principalTable: "wallets",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "orders",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    channel = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reseller_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reseller_customer_id = table.Column<Guid>(type: "uuid", nullable: true),
                    fulfillment_branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    delivery_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    delivery_mobile = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    delivery_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    delivery_address_line = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    delivery_city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    delivery_state = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    delivery_pin = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    merchandise_total = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    shipping_fee = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    grand_total = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    wallet_ledger_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    placed_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_orders", x => x.id);
                    table.CheckConstraint("ck_orders_totals", "grand_total = merchandise_total + shipping_fee AND merchandise_total >= 0 AND shipping_fee >= 0");
                });

            migrationBuilder.CreateTable(
                name: "reseller_customers",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reseller_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    mobile = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    address_line = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    state = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    pin = table.Column<string>(type: "character varying(6)", maxLength: 6, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reseller_customers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "deposit_requests",
                schema: "wallet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reseller_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    method = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    proof_file_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reseller_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    submitted_by = table.Column<Guid>(type: "uuid", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    review_note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ledger_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_deposit_requests", x => x.id);
                    table.CheckConstraint("ck_deposit_amount_positive", "amount > 0");
                    table.ForeignKey(
                        name: "fk_deposit_requests_wallet_file_proof_file_id",
                        column: x => x.proof_file_id,
                        principalSchema: "wallet",
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_lines",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    product_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    variant_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    retail_price_id = table.Column<Guid>(type: "uuid", nullable: true),
                    retail_unit_price = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    discount_source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    discount_pct = table.Column<decimal>(type: "numeric(7,4)", precision: 7, scale: 4, nullable: false),
                    discount_amount_per_unit = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    final_unit_price = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    line_total = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    commercial_term_id = table.Column<Guid>(type: "uuid", nullable: true),
                    commercial_term_version = table.Column<int>(type: "integer", nullable: true),
                    product_discount_id = table.Column<Guid>(type: "uuid", nullable: true),
                    cost_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    item_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_lines", x => x.id);
                    table.CheckConstraint("ck_order_lines_amounts", "quantity > 0 AND final_unit_price >= 0 AND final_unit_price <= retail_unit_price AND line_total = final_unit_price * quantity");
                    table.ForeignKey(
                        name: "fk_order_lines_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "orders",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "order_status_changes",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    to_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    note = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_order_status_changes", x => x.id);
                    table.ForeignKey(
                        name: "fk_order_status_changes_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "orders",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_deposit_requests_number",
                schema: "wallet",
                table: "deposit_requests",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_deposit_requests_proof_file_id",
                schema: "wallet",
                table: "deposit_requests",
                column: "proof_file_id");

            migrationBuilder.CreateIndex(
                name: "ix_deposit_requests_reseller_id_submitted_at",
                schema: "wallet",
                table: "deposit_requests",
                columns: new[] { "reseller_id", "submitted_at" });

            migrationBuilder.CreateIndex(
                name: "ix_deposit_requests_status",
                schema: "wallet",
                table: "deposit_requests",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ux_deposit_reference_active",
                schema: "wallet",
                table: "deposit_requests",
                column: "reference",
                unique: true,
                filter: "status <> 'Rejected'");

            migrationBuilder.CreateIndex(
                name: "ix_fulfillment_inquiries_reference",
                schema: "orders",
                table: "fulfillment_inquiries",
                column: "reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fulfillment_inquiries_status_created_at",
                schema: "orders",
                table: "fulfillment_inquiries",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_deposit_request_id",
                schema: "wallet",
                table: "ledger_entries",
                column: "deposit_request_id",
                unique: true,
                filter: "deposit_request_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_reseller_id_seq",
                schema: "wallet",
                table: "ledger_entries",
                columns: new[] { "reseller_id", "seq" });

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_reverses_entry_id",
                schema: "wallet",
                table: "ledger_entries",
                column: "reverses_entry_id",
                unique: true,
                filter: "reverses_entry_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_wallet_id",
                schema: "wallet",
                table: "ledger_entries",
                column: "wallet_id");

            migrationBuilder.CreateIndex(
                name: "ux_ledger_one_debit_per_order",
                schema: "wallet",
                table: "ledger_entries",
                column: "order_id",
                unique: true,
                filter: "type = 'Debit' AND order_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_order_lines_order_id",
                schema: "orders",
                table: "order_lines",
                column: "order_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_lines_sku_id",
                schema: "orders",
                table: "order_lines",
                column: "sku_id");

            migrationBuilder.CreateIndex(
                name: "ix_order_status_changes_order_id_occurred_at",
                schema: "orders",
                table: "order_status_changes",
                columns: new[] { "order_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_fulfillment_branch_id_status",
                schema: "orders",
                table: "orders",
                columns: new[] { "fulfillment_branch_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_number",
                schema: "orders",
                table: "orders",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_orders_reseller_id_created_at",
                schema: "orders",
                table: "orders",
                columns: new[] { "reseller_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_reseller_customers_reseller_id",
                schema: "orders",
                table: "reseller_customers",
                column: "reseller_id");
            // Cross-module integrity (checked at commit).
            migrationBuilder.Sql("ALTER TABLE orders.orders ADD CONSTRAINT fk_orders_reseller FOREIGN KEY (reseller_id) REFERENCES resellers.resellers (id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE orders.orders ADD CONSTRAINT fk_orders_branch FOREIGN KEY (fulfillment_branch_id) REFERENCES branches.branches (id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE orders.order_lines ADD CONSTRAINT fk_order_lines_sku FOREIGN KEY (sku_id) REFERENCES catalog.skus (id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE orders.reseller_customers ADD CONSTRAINT fk_reseller_customers_reseller FOREIGN KEY (reseller_id) REFERENCES resellers.resellers (id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE orders.fulfillment_inquiries ADD CONSTRAINT fk_fulfillment_inquiries_reseller FOREIGN KEY (reseller_id) REFERENCES resellers.resellers (id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE wallet.ledger_entries ADD CONSTRAINT fk_ledger_entries_order FOREIGN KEY (order_id) REFERENCES orders.orders (id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE wallet.deposit_requests ADD CONSTRAINT fk_deposit_requests_reseller FOREIGN KEY (reseller_id) REFERENCES resellers.resellers (id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("CREATE UNIQUE INDEX ux_reseller_customers_mobile ON orders.reseller_customers (reseller_id, mobile);");

            // Immutable financial and order history (SPEC §16, §15, §30).
            migrationBuilder.Sql(AppendOnly.Protect("wallet", "ledger_entries"));
            migrationBuilder.Sql(AppendOnly.Protect("orders", "order_lines"));
            migrationBuilder.Sql(AppendOnly.Protect("orders", "order_status_changes"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(AppendOnly.Unprotect("orders", "order_status_changes"));
            migrationBuilder.Sql(AppendOnly.Unprotect("orders", "order_lines"));
            migrationBuilder.Sql(AppendOnly.Unprotect("wallet", "ledger_entries"));
            migrationBuilder.Sql("DROP INDEX IF EXISTS orders.ux_reseller_customers_mobile;");
            migrationBuilder.Sql("ALTER TABLE wallet.deposit_requests DROP CONSTRAINT IF EXISTS fk_deposit_requests_reseller;");
            migrationBuilder.Sql("ALTER TABLE wallet.ledger_entries DROP CONSTRAINT IF EXISTS fk_ledger_entries_order;");
            migrationBuilder.Sql("ALTER TABLE orders.fulfillment_inquiries DROP CONSTRAINT IF EXISTS fk_fulfillment_inquiries_reseller;");
            migrationBuilder.Sql("ALTER TABLE orders.reseller_customers DROP CONSTRAINT IF EXISTS fk_reseller_customers_reseller;");
            migrationBuilder.Sql("ALTER TABLE orders.order_lines DROP CONSTRAINT IF EXISTS fk_order_lines_sku;");
            migrationBuilder.Sql("ALTER TABLE orders.orders DROP CONSTRAINT IF EXISTS fk_orders_branch;");
            migrationBuilder.Sql("ALTER TABLE orders.orders DROP CONSTRAINT IF EXISTS fk_orders_reseller;");

            migrationBuilder.DropTable(
                name: "deposit_requests",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "fulfillment_inquiries",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "ledger_entries",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "order_lines",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "order_status_changes",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "reseller_customers",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "files",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "orders",
                schema: "orders");

            migrationBuilder.DropSequence(
                name: "deposit_seq",
                schema: "wallet");

            migrationBuilder.DropSequence(
                name: "inquiry_number_seq",
                schema: "orders");

            migrationBuilder.DropSequence(
                name: "order_number_seq",
                schema: "orders");
        }
    }
}
