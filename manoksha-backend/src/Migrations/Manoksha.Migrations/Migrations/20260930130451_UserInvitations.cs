using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Manoksha.Migrations.Migrations
{
    /// <inheritdoc />
    public partial class UserInvitations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "user_invitations",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_invitations", x => x.id);
                    table.ForeignKey(
                        name: "fk_user_invitations_users_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_user_invitations_token_hash",
                schema: "identity",
                table: "user_invitations",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_user_invitations_user_id",
                schema: "identity",
                table: "user_invitations",
                column: "user_id");

            migrationBuilder.Sql("ALTER TABLE identity.user_invitations ADD CONSTRAINT fk_user_invitations_created_by FOREIGN KEY (created_by) REFERENCES identity.users (id) ON DELETE RESTRICT;");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "user_invitations",
                schema: "identity");
        }
    }
}
