using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AjoCircle.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "role",
                table: "users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "Member");

            // Existing accounts: anyone already running a group or verified as an organizer keeps organizer access.
            migrationBuilder.Sql("""
                UPDATE users SET role = 'Organizer'
                WHERE identity_level = 'Organizer' OR id IN (SELECT organizer_id FROM groups);
                """);
            migrationBuilder.Sql("ALTER TABLE users ADD CONSTRAINT ck_users_role CHECK (role IN ('Member', 'Organizer'));");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE users DROP CONSTRAINT IF EXISTS ck_users_role;");
            migrationBuilder.DropColumn(
                name: "role",
                table: "users");
        }
    }
}
