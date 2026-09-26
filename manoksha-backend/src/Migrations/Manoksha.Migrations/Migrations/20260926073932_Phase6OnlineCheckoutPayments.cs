using System;
using Manoksha.Persistence.Platform;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Manoksha.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Phase6OnlineCheckoutPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "payments");

            migrationBuilder.CreateSequence(
                name: "reconciliation_seq",
                schema: "payments");

            migrationBuilder.AddColumn<Guid>(
                name: "customer_user_id",
                schema: "orders",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "payment_attempt_id",
                schema: "orders",
                table: "orders",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<decimal>(
                name: "cost_amount",
                schema: "orders",
                table: "order_lines",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(14,2)",
                oldPrecision: 14,
                oldScale: 2);

            migrationBuilder.AddColumn<Guid>(
                name: "online_deposit_id",
                schema: "wallet",
                table: "ledger_entries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "online_deposits",
                schema: "wallet",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    reseller_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    payment_attempt_id = table.Column<Guid>(type: "uuid", nullable: true),
                    ledger_entry_id = table.Column<Guid>(type: "uuid", nullable: true),
                    provider_payment_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    requested_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_online_deposits", x => x.id);
                    table.CheckConstraint("ck_online_deposit_amount_positive", "amount > 0");
                });

            migrationBuilder.CreateTable(
                name: "payment_attempts",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    payer_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    provider_order_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    provider_payment_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    redirect_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    return_path = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    initiated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    next_poll_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_attempts", x => x.id);
                    table.CheckConstraint("ck_payment_attempts_amount_positive", "amount > 0");
                });

            migrationBuilder.CreateTable(
                name: "provider_events",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    provider_event_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    provider_order_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    event_type = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    processing_result = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_provider_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "reservations",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    close_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reservations", x => x.id);
                    table.ForeignKey(
                        name: "fk_reservations_orders_order_id",
                        column: x => x.order_id,
                        principalSchema: "orders",
                        principalTable: "orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "simulator_transactions",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    merchant_attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider_order_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    return_path = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    paid_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    provider_payment_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_simulator_transactions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "payment_status_changes",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    to_status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    source = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payment_status_changes", x => x.id);
                    table.ForeignKey(
                        name: "fk_payment_status_changes_payment_attempts_attempt_id",
                        column: x => x.attempt_id,
                        principalSchema: "payments",
                        principalTable: "payment_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reconciliations",
                schema: "payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    case_number = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    payment_attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    provider_order_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    provider_payment_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    expected_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    paid_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    payer_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    purpose = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reference_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference_number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    reason_code = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    detail = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    owner_action = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    external_refund_ref = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reconciliations", x => x.id);
                    table.ForeignKey(
                        name: "fk_reconciliations_payment_attempts_payment_attempt_id",
                        column: x => x.payment_attempt_id,
                        principalSchema: "payments",
                        principalTable: "payment_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "reservation_lines",
                schema: "orders",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reservation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    order_line_id = table.Column<Guid>(type: "uuid", nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    quantity = table.Column<int>(type: "integer", nullable: false),
                    item_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    cost_amount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reservation_lines", x => x.id);
                    table.CheckConstraint("ck_reservation_lines_quantity", "quantity > 0");
                    table.ForeignKey(
                        name: "fk_reservation_lines_order_lines_order_line_id",
                        column: x => x.order_line_id,
                        principalSchema: "orders",
                        principalTable: "order_lines",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reservation_lines_reservations_reservation_id",
                        column: x => x.reservation_id,
                        principalSchema: "orders",
                        principalTable: "reservations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_orders_customer_user_id_created_at",
                schema: "orders",
                table: "orders",
                columns: new[] { "customer_user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_orders_payment_attempt_id",
                schema: "orders",
                table: "orders",
                column: "payment_attempt_id",
                unique: true,
                filter: "payment_attempt_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_ledger_entries_online_deposit_id",
                schema: "wallet",
                table: "ledger_entries",
                column: "online_deposit_id",
                unique: true,
                filter: "online_deposit_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_online_deposits_ledger_entry_id",
                schema: "wallet",
                table: "online_deposits",
                column: "ledger_entry_id",
                unique: true,
                filter: "ledger_entry_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_online_deposits_number",
                schema: "wallet",
                table: "online_deposits",
                column: "number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_online_deposits_reseller_id_created_at",
                schema: "wallet",
                table: "online_deposits",
                columns: new[] { "reseller_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_payment_attempts_provider_provider_order_ref",
                schema: "payments",
                table: "payment_attempts",
                columns: new[] { "provider", "provider_order_ref" },
                unique: true,
                filter: "provider_order_ref IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payment_attempts_provider_provider_payment_ref",
                schema: "payments",
                table: "payment_attempts",
                columns: new[] { "provider", "provider_payment_ref" },
                unique: true,
                filter: "provider_payment_ref IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_payment_attempts_status_next_poll_at",
                schema: "payments",
                table: "payment_attempts",
                columns: new[] { "status", "next_poll_at" });

            migrationBuilder.CreateIndex(
                name: "ux_payment_attempts_one_live",
                schema: "payments",
                table: "payment_attempts",
                columns: new[] { "purpose", "reference_id" },
                unique: true,
                filter: "status IN ('Initiated', 'Pending')");

            migrationBuilder.CreateIndex(
                name: "ix_payment_status_changes_attempt_id_occurred_at",
                schema: "payments",
                table: "payment_status_changes",
                columns: new[] { "attempt_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_provider_events_processed_at",
                schema: "payments",
                table: "provider_events",
                column: "processed_at",
                filter: "processed_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_provider_events_provider_provider_event_id",
                schema: "payments",
                table: "provider_events",
                columns: new[] { "provider", "provider_event_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reconciliations_case_number",
                schema: "payments",
                table: "reconciliations",
                column: "case_number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reconciliations_payment_attempt_id",
                schema: "payments",
                table: "reconciliations",
                column: "payment_attempt_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reconciliations_status_created_at",
                schema: "payments",
                table: "reconciliations",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_reservation_lines_order_line_id",
                schema: "orders",
                table: "reservation_lines",
                column: "order_line_id");

            migrationBuilder.CreateIndex(
                name: "ix_reservation_lines_reservation_id",
                schema: "orders",
                table: "reservation_lines",
                column: "reservation_id");

            migrationBuilder.CreateIndex(
                name: "ix_reservations_active_expiry",
                schema: "orders",
                table: "reservations",
                column: "expires_at",
                filter: "status = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ux_reservations_one_active_per_order",
                schema: "orders",
                table: "reservations",
                column: "order_id",
                unique: true,
                filter: "status = 'Active'");

            migrationBuilder.CreateIndex(
                name: "ix_simulator_transactions_merchant_attempt_id",
                schema: "payments",
                table: "simulator_transactions",
                column: "merchant_attempt_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_simulator_transactions_provider_order_ref",
                schema: "payments",
                table: "simulator_transactions",
                column: "provider_order_ref",
                unique: true);

            // Cross-module references are checked at commit (EF cannot order inserts across module models).
            migrationBuilder.Sql("ALTER TABLE orders.orders ADD CONSTRAINT fk_orders_customer_user FOREIGN KEY (customer_user_id) REFERENCES identity.users (id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE orders.orders ADD CONSTRAINT fk_orders_payment_attempt FOREIGN KEY (payment_attempt_id) REFERENCES payments.payment_attempts (id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE orders.reservations ADD CONSTRAINT fk_reservations_branch FOREIGN KEY (branch_id) REFERENCES branches.branches (id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE orders.reservation_lines ADD CONSTRAINT fk_reservation_lines_sku FOREIGN KEY (sku_id) REFERENCES catalog.skus (id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE payments.payment_attempts ADD CONSTRAINT fk_payment_attempts_payer FOREIGN KEY (payer_user_id) REFERENCES identity.users (id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE wallet.online_deposits ADD CONSTRAINT fk_online_deposits_reseller FOREIGN KEY (reseller_id) REFERENCES resellers.resellers (id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE wallet.online_deposits ADD CONSTRAINT fk_online_deposits_payment_attempt FOREIGN KEY (payment_attempt_id) REFERENCES payments.payment_attempts (id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;");
            migrationBuilder.Sql("ALTER TABLE wallet.ledger_entries ADD CONSTRAINT fk_ledger_entries_online_deposit FOREIGN KEY (online_deposit_id) REFERENCES wallet.online_deposits (id) ON DELETE RESTRICT;");
            migrationBuilder.Sql("ALTER TABLE wallet.online_deposits ADD CONSTRAINT fk_online_deposits_ledger_entry FOREIGN KEY (ledger_entry_id) REFERENCES wallet.ledger_entries (id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;");

            // Payment status history is evidence: append-only (design §15).
            migrationBuilder.Sql(AppendOnly.Protect("payments", "payment_status_changes"));
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(AppendOnly.Unprotect("payments", "payment_status_changes"));
            migrationBuilder.Sql("ALTER TABLE wallet.online_deposits DROP CONSTRAINT IF EXISTS fk_online_deposits_ledger_entry;");
            migrationBuilder.Sql("ALTER TABLE wallet.ledger_entries DROP CONSTRAINT IF EXISTS fk_ledger_entries_online_deposit;");
            migrationBuilder.Sql("ALTER TABLE wallet.online_deposits DROP CONSTRAINT IF EXISTS fk_online_deposits_payment_attempt;");
            migrationBuilder.Sql("ALTER TABLE wallet.online_deposits DROP CONSTRAINT IF EXISTS fk_online_deposits_reseller;");
            migrationBuilder.Sql("ALTER TABLE payments.payment_attempts DROP CONSTRAINT IF EXISTS fk_payment_attempts_payer;");
            migrationBuilder.Sql("ALTER TABLE orders.reservation_lines DROP CONSTRAINT IF EXISTS fk_reservation_lines_sku;");
            migrationBuilder.Sql("ALTER TABLE orders.reservations DROP CONSTRAINT IF EXISTS fk_reservations_branch;");
            migrationBuilder.Sql("ALTER TABLE orders.orders DROP CONSTRAINT IF EXISTS fk_orders_payment_attempt;");
            migrationBuilder.Sql("ALTER TABLE orders.orders DROP CONSTRAINT IF EXISTS fk_orders_customer_user;");

            migrationBuilder.DropTable(
                name: "online_deposits",
                schema: "wallet");

            migrationBuilder.DropTable(
                name: "payment_status_changes",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "provider_events",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "reconciliations",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "reservation_lines",
                schema: "orders");

            migrationBuilder.DropTable(
                name: "simulator_transactions",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "payment_attempts",
                schema: "payments");

            migrationBuilder.DropTable(
                name: "reservations",
                schema: "orders");

            migrationBuilder.DropIndex(
                name: "ix_orders_customer_user_id_created_at",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "ix_orders_payment_attempt_id",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropIndex(
                name: "ix_ledger_entries_online_deposit_id",
                schema: "wallet",
                table: "ledger_entries");

            migrationBuilder.DropColumn(
                name: "customer_user_id",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "payment_attempt_id",
                schema: "orders",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "online_deposit_id",
                schema: "wallet",
                table: "ledger_entries");

            migrationBuilder.DropSequence(
                name: "reconciliation_seq",
                schema: "payments");

            migrationBuilder.AlterColumn<decimal>(
                name: "cost_amount",
                schema: "orders",
                table: "order_lines",
                type: "numeric(14,2)",
                precision: 14,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(14,2)",
                oldPrecision: 14,
                oldScale: 2,
                oldNullable: true);
        }
    }
}
