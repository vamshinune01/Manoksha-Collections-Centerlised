using System;
using Manoksha.Persistence.Platform;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Manoksha.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class Phase2BranchesEmployeesCatalog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "employees");

            migrationBuilder.EnsureSchema(
                name: "catalog");

            migrationBuilder.EnsureSchema(
                name: "branches");

            migrationBuilder.CreateSequence(
                name: "employee_code_seq",
                schema: "employees");

            migrationBuilder.CreateSequence(
                name: "internal_barcode_seq",
                schema: "catalog");

            migrationBuilder.CreateSequence(
                name: "sku_code_seq",
                schema: "catalog");

            migrationBuilder.CreateTable(
                name: "attribute_definitions",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attribute_definitions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "branches",
                schema: "branches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    address_line1 = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    city = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    state = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    pin = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    phone = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_branches", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "categories",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    parent_id = table.Column<Guid>(type: "uuid", nullable: true),
                    name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_categories", x => x.id);
                    table.ForeignKey(
                        name: "fk_categories_categories_parent_id",
                        column: x => x.parent_id,
                        principalSchema: "catalog",
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "employees",
                schema: "employees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    full_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    mobile_e164 = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    assigned_branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    joined_on = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employees", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "fulfillment_priority_versions",
                schema: "branches",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    version_no = table.Column<int>(type: "integer", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fulfillment_priority_versions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "attribute_options",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    attribute_id = table.Column<Guid>(type: "uuid", nullable: false),
                    value = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attribute_options", x => x.id);
                    table.ForeignKey(
                        name: "fk_attribute_options_attribute_definitions_attribute_id",
                        column: x => x.attribute_id,
                        principalSchema: "catalog",
                        principalTable: "attribute_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "products",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    slug = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    tracking_mode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    available_for_retail = table.Column<bool>(type: "boolean", nullable: false),
                    available_for_reseller = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_products", x => x.id);
                    table.ForeignKey(
                        name: "fk_products_categories_category_id",
                        column: x => x.category_id,
                        principalSchema: "catalog",
                        principalTable: "categories",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "attendance_records",
                schema: "employees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    clock_in_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    clock_out_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    clock_in_source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    clock_out_source = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    corrected_by = table.Column<Guid>(type: "uuid", nullable: true),
                    corrected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    correction_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_attendance_records", x => x.id);
                    table.ForeignKey(
                        name: "fk_attendance_records_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "employees",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "employee_branch_assignments",
                schema: "employees",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    employee_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    to_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    assigned_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_employee_branch_assignments", x => x.id);
                    table.ForeignKey(
                        name: "fk_employee_branch_assignments_employees_employee_id",
                        column: x => x.employee_id,
                        principalSchema: "employees",
                        principalTable: "employees",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "fulfillment_priority_entries",
                schema: "branches",
                columns: table => new
                {
                    version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    branch_id = table.Column<Guid>(type: "uuid", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fulfillment_priority_entries", x => new { x.version_id, x.branch_id });
                    table.ForeignKey(
                        name: "fk_fulfillment_priority_entries_branches_branch_id",
                        column: x => x.branch_id,
                        principalSchema: "branches",
                        principalTable: "branches",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_fulfillment_priority_entries_fulfillment_priority_versions_",
                        column: x => x.version_id,
                        principalSchema: "branches",
                        principalTable: "fulfillment_priority_versions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "product_variant_attributes",
                schema: "catalog",
                columns: table => new
                {
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attribute_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_variant_attributes", x => new { x.product_id, x.attribute_id });
                    table.ForeignKey(
                        name: "fk_product_variant_attributes_attribute_definitions_attribute_",
                        column: x => x.attribute_id,
                        principalSchema: "catalog",
                        principalTable: "attribute_definitions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_product_variant_attributes_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "variants",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    combination_key = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_variants", x => x.id);
                    table.ForeignKey(
                        name: "fk_variants_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "skus",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_skus", x => x.id);
                    table.ForeignKey(
                        name: "fk_skus_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_skus_variants_variant_id",
                        column: x => x.variant_id,
                        principalSchema: "catalog",
                        principalTable: "variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "variant_attribute_values",
                schema: "catalog",
                columns: table => new
                {
                    variant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    attribute_id = table.Column<Guid>(type: "uuid", nullable: false),
                    option_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_variant_attribute_values", x => new { x.variant_id, x.attribute_id });
                    table.ForeignKey(
                        name: "fk_variant_attribute_values_attribute_options_option_id",
                        column: x => x.option_id,
                        principalSchema: "catalog",
                        principalTable: "attribute_options",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_variant_attribute_values_variants_variant_id",
                        column: x => x.variant_id,
                        principalSchema: "catalog",
                        principalTable: "variants",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "barcodes",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    sku_id = table.Column<Guid>(type: "uuid", nullable: false),
                    inventory_item_id = table.Column<Guid>(type: "uuid", nullable: true),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    retired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    retired_by = table.Column<Guid>(type: "uuid", nullable: true),
                    retire_reason = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_barcodes", x => x.id);
                    table.ForeignKey(
                        name: "fk_barcodes_skus_sku_id",
                        column: x => x.sku_id,
                        principalSchema: "catalog",
                        principalTable: "skus",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "barcode_prints",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    barcode_id = table.Column<Guid>(type: "uuid", nullable: false),
                    copies = table.Column<int>(type: "integer", nullable: false),
                    is_reprint = table.Column<bool>(type: "boolean", nullable: false),
                    printed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    printed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_barcode_prints", x => x.id);
                    table.ForeignKey(
                        name: "fk_barcode_prints_barcodes_barcode_id",
                        column: x => x.barcode_id,
                        principalSchema: "catalog",
                        principalTable: "barcodes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_records_branch_id_clock_in_at",
                schema: "employees",
                table: "attendance_records",
                columns: new[] { "branch_id", "clock_in_at" });

            migrationBuilder.CreateIndex(
                name: "ix_attendance_records_employee_id_clock_in_at",
                schema: "employees",
                table: "attendance_records",
                columns: new[] { "employee_id", "clock_in_at" });

            migrationBuilder.CreateIndex(
                name: "ux_attendance_one_open_per_employee",
                schema: "employees",
                table: "attendance_records",
                column: "employee_id",
                unique: true,
                filter: "clock_out_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_attribute_definitions_code",
                schema: "catalog",
                table: "attribute_definitions",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_attribute_options_attribute_id_value",
                schema: "catalog",
                table: "attribute_options",
                columns: new[] { "attribute_id", "value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_barcode_prints_barcode_id_printed_at",
                schema: "catalog",
                table: "barcode_prints",
                columns: new[] { "barcode_id", "printed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_barcodes_code",
                schema: "catalog",
                table: "barcodes",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_barcodes_inventory_item_id",
                schema: "catalog",
                table: "barcodes",
                column: "inventory_item_id",
                unique: true,
                filter: "inventory_item_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_barcodes_sku_id",
                schema: "catalog",
                table: "barcodes",
                column: "sku_id");

            migrationBuilder.CreateIndex(
                name: "ix_branches_code",
                schema: "branches",
                table: "branches",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_categories_parent_id",
                schema: "catalog",
                table: "categories",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_categories_slug",
                schema: "catalog",
                table: "categories",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employee_branch_assignments_employee_id_from_at",
                schema: "employees",
                table: "employee_branch_assignments",
                columns: new[] { "employee_id", "from_at" });

            migrationBuilder.CreateIndex(
                name: "ix_employees_assigned_branch_id",
                schema: "employees",
                table: "employees",
                column: "assigned_branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_employees_employee_code",
                schema: "employees",
                table: "employees",
                column: "employee_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_employees_user_id",
                schema: "employees",
                table: "employees",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fulfillment_priority_entries_branch_id",
                schema: "branches",
                table: "fulfillment_priority_entries",
                column: "branch_id");

            migrationBuilder.CreateIndex(
                name: "ix_fulfillment_priority_entries_version_id_priority",
                schema: "branches",
                table: "fulfillment_priority_entries",
                columns: new[] { "version_id", "priority" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_fulfillment_priority_versions_version_no",
                schema: "branches",
                table: "fulfillment_priority_versions",
                column: "version_no",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_product_variant_attributes_attribute_id",
                schema: "catalog",
                table: "product_variant_attributes",
                column: "attribute_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_category_id",
                schema: "catalog",
                table: "products",
                column: "category_id");

            migrationBuilder.CreateIndex(
                name: "ix_products_name",
                schema: "catalog",
                table: "products",
                column: "name");

            migrationBuilder.CreateIndex(
                name: "ix_products_slug",
                schema: "catalog",
                table: "products",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_skus_code",
                schema: "catalog",
                table: "skus",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_skus_product_id",
                schema: "catalog",
                table: "skus",
                column: "product_id");

            migrationBuilder.CreateIndex(
                name: "ix_skus_variant_id",
                schema: "catalog",
                table: "skus",
                column: "variant_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_variant_attribute_values_option_id",
                schema: "catalog",
                table: "variant_attribute_values",
                column: "option_id");

            migrationBuilder.CreateIndex(
                name: "ix_variants_product_id_combination_key",
                schema: "catalog",
                table: "variants",
                columns: new[] { "product_id", "combination_key" },
                unique: true);
            // Cross-module integrity (modules stay decoupled in code; the single database still enforces references).
            migrationBuilder.Sql("ALTER TABLE identity.user_role_assignments ADD CONSTRAINT fk_user_role_assignments_branch FOREIGN KEY (branch_id) REFERENCES branches.branches (id) ON DELETE RESTRICT;");
            migrationBuilder.Sql("ALTER TABLE employees.employees ADD CONSTRAINT fk_employees_branch FOREIGN KEY (assigned_branch_id) REFERENCES branches.branches (id) ON DELETE RESTRICT;");
            migrationBuilder.Sql("ALTER TABLE employees.employees ADD CONSTRAINT fk_employees_user FOREIGN KEY (user_id) REFERENCES identity.users (id) ON DELETE RESTRICT;");
            migrationBuilder.Sql("ALTER TABLE employees.employee_branch_assignments ADD CONSTRAINT fk_employee_branch_assignments_branch FOREIGN KEY (branch_id) REFERENCES branches.branches (id) ON DELETE RESTRICT;");
            migrationBuilder.Sql("ALTER TABLE employees.attendance_records ADD CONSTRAINT fk_attendance_records_branch FOREIGN KEY (branch_id) REFERENCES branches.branches (id) ON DELETE RESTRICT;");

            // Immutable history.
            migrationBuilder.Sql(AppendOnly.Protect("branches", "fulfillment_priority_versions"));
            migrationBuilder.Sql(AppendOnly.Protect("branches", "fulfillment_priority_entries"));
            migrationBuilder.Sql(AppendOnly.Protect("catalog", "barcode_prints"));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(AppendOnly.Unprotect("catalog", "barcode_prints"));
            migrationBuilder.Sql(AppendOnly.Unprotect("branches", "fulfillment_priority_entries"));
            migrationBuilder.Sql(AppendOnly.Unprotect("branches", "fulfillment_priority_versions"));
            migrationBuilder.Sql("ALTER TABLE employees.attendance_records DROP CONSTRAINT IF EXISTS fk_attendance_records_branch;");
            migrationBuilder.Sql("ALTER TABLE employees.employee_branch_assignments DROP CONSTRAINT IF EXISTS fk_employee_branch_assignments_branch;");
            migrationBuilder.Sql("ALTER TABLE employees.employees DROP CONSTRAINT IF EXISTS fk_employees_user;");
            migrationBuilder.Sql("ALTER TABLE employees.employees DROP CONSTRAINT IF EXISTS fk_employees_branch;");
            migrationBuilder.Sql("ALTER TABLE identity.user_role_assignments DROP CONSTRAINT IF EXISTS fk_user_role_assignments_branch;");

            migrationBuilder.DropTable(
                name: "attendance_records",
                schema: "employees");

            migrationBuilder.DropTable(
                name: "barcode_prints",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "employee_branch_assignments",
                schema: "employees");

            migrationBuilder.DropTable(
                name: "fulfillment_priority_entries",
                schema: "branches");

            migrationBuilder.DropTable(
                name: "product_variant_attributes",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "variant_attribute_values",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "barcodes",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "employees",
                schema: "employees");

            migrationBuilder.DropTable(
                name: "branches",
                schema: "branches");

            migrationBuilder.DropTable(
                name: "fulfillment_priority_versions",
                schema: "branches");

            migrationBuilder.DropTable(
                name: "attribute_options",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "skus",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "attribute_definitions",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "variants",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "products",
                schema: "catalog");

            migrationBuilder.DropTable(
                name: "categories",
                schema: "catalog");

            migrationBuilder.DropSequence(
                name: "employee_code_seq",
                schema: "employees");

            migrationBuilder.DropSequence(
                name: "internal_barcode_seq",
                schema: "catalog");

            migrationBuilder.DropSequence(
                name: "sku_code_seq",
                schema: "catalog");
        }
    }
}
