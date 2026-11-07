using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AjoCircle.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NotificationsAndRecovery : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "email_notifications_enabled",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: true); // existing users keep receiving notification emails, same as new accounts

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    title = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    body = table.Column<string>(type: "character varying(600)", maxLength: 600, nullable: false),
                    link_path = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    dedupe_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    read_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    email_status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    email_attempts = table.Column<int>(type: "integer", nullable: false),
                    next_email_attempt_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    email_sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    email_last_error = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                    table.CheckConstraint("ck_notifications_attempts", "email_attempts >= 0");
                    table.CheckConstraint("ck_notifications_email_status", "email_status IN ('Pending', 'Sent', 'Failed', 'Skipped')");
                    table.CheckConstraint("ck_notifications_type", "type IN ('MemberJoined', 'GroupReadyToStart', 'GroupStarted', 'GroupCancelled', 'ContributionRecorded', 'ContributionCorrected', 'PayoutRecorded', 'PayoutCorrected', 'DisputeRaised', 'DisputeReply', 'DisputeResolved', 'ContributionDueSoon', 'ContributionOverdue', 'PayoutApproaching')");
                    table.ForeignKey(
                        name: "fk_notifications_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_notifications_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "password_reset_tokens",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_password_reset_tokens", x => x.id);
                    table.ForeignKey(
                        name: "fk_password_reset_tokens_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_group_id",
                table: "notifications",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_next_email_attempt_at",
                table: "notifications",
                column: "next_email_attempt_at",
                filter: "email_status = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_id_created_at",
                table: "notifications",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_id_dedupe_key",
                table: "notifications",
                columns: new[] { "user_id", "dedupe_key" },
                unique: true,
                filter: "dedupe_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_password_reset_tokens_token_hash",
                table: "password_reset_tokens",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_password_reset_tokens_user_id_created_at",
                table: "password_reset_tokens",
                columns: new[] { "user_id", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "password_reset_tokens");

            migrationBuilder.DropColumn(
                name: "email_notifications_enabled",
                table: "users");
        }
    }
}
