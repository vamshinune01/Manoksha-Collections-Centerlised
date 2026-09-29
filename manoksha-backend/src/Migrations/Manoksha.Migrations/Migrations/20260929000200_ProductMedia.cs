using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Manoksha.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class ProductMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "product_media",
                schema: "catalog",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    product_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    status = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    alt_text = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    original_key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    original_file_name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    original_content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    original_bytes = table.Column<long>(type: "bigint", nullable: false),
                    width = table.Column<int>(type: "integer", nullable: true),
                    height = table.Column<int>(type: "integer", nullable: true),
                    duration_seconds = table.Column<double>(type: "double precision", nullable: true),
                    renditions = table.Column<string>(type: "jsonb", nullable: false),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_product_media", x => x.id);
                    table.CheckConstraint("ck_product_media_bytes", "original_bytes >= 0");
                    table.ForeignKey(
                        name: "fk_product_media_products_product_id",
                        column: x => x.product_id,
                        principalSchema: "catalog",
                        principalTable: "products",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_product_media_product_id_status_sort_order",
                schema: "catalog",
                table: "product_media",
                columns: new[] { "product_id", "status", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_product_media_status_created_at",
                schema: "catalog",
                table: "product_media",
                columns: new[] { "status", "created_at" });

            migrationBuilder.Sql("ALTER TABLE catalog.product_media ADD CONSTRAINT fk_product_media_created_by FOREIGN KEY (created_by) REFERENCES identity.users (id) ON DELETE RESTRICT DEFERRABLE INITIALLY DEFERRED;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE catalog.product_media DROP CONSTRAINT IF EXISTS fk_product_media_created_by;");

            migrationBuilder.DropTable(
                name: "product_media",
                schema: "catalog");
        }
    }
}
