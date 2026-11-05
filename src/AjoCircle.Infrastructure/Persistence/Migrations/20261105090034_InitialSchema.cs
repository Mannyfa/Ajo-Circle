using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AjoCircle.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "audit_logs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    actor_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: true),
                    metadata = table.Column<string>(type: "jsonb", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_logs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    display_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    password_hash = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    contact_verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    terms_accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    security_stamp = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "contact_verifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    code_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    consumed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contact_verifications", x => x.id);
                    table.ForeignKey(
                        name: "fk_contact_verifications_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "contribution_obligations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    round_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contribution_obligations", x => x.id);
                    table.CheckConstraint("ck_contribution_obligations_amount", "amount_minor >= 0");
                    table.CheckConstraint("ck_contribution_obligations_currency", "currency = 'NGN'");
                    table.CheckConstraint("ck_contribution_obligations_status", "status IN ('Due', 'SubmittedForReview', 'RecordedByOrganizer', 'ConfirmedByMember', 'VerifiedByProvider', 'Disputed', 'Reversed')");
                });

            migrationBuilder.CreateTable(
                name: "group_invitations",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    token_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    max_uses = table.Column<int>(type: "integer", nullable: true),
                    use_count = table.Column<int>(type: "integer", nullable: false),
                    revoked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    revoked_by = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_group_invitations", x => x.id);
                    table.CheckConstraint("ck_group_invitations_use_count", "use_count >= 0 AND (max_uses IS NULL OR use_count <= max_uses)");
                });

            migrationBuilder.CreateTable(
                name: "group_memberships",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    join_sequence = table.Column<int>(type: "integer", nullable: false),
                    accepted_scheme_version_id = table.Column<Guid>(type: "uuid", nullable: false),
                    joined_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    terms_accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    joined_via_invitation_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_group_memberships", x => x.id);
                    table.CheckConstraint("ck_group_memberships_join_sequence", "join_sequence >= 1");
                    table.CheckConstraint("ck_group_memberships_status", "status IN ('Active', 'Removed')");
                    table.ForeignKey(
                        name: "fk_group_memberships_group_invitations_joined_via_invitation_id",
                        column: x => x.joined_via_invitation_id,
                        principalTable: "group_invitations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_group_memberships_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "groups",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    organizer_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    scheme_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    draft_rules_json = table.Column<string>(type: "jsonb", nullable: true),
                    current_scheme_version_id = table.Column<Guid>(type: "uuid", nullable: true),
                    next_join_sequence = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    enrolment_closed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancelled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    cancellation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_groups", x => x.id);
                    table.CheckConstraint("ck_groups_currency", "currency = 'NGN'");
                    table.CheckConstraint("ck_groups_status", "status IN ('Draft', 'OpenForJoining', 'ReadyToStart', 'Active', 'Suspended', 'Completed', 'Cancelled')");
                    table.ForeignKey(
                        name: "fk_groups_users_organizer_id",
                        column: x => x.organizer_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payout_positions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    assigned_by = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payout_positions", x => x.id);
                    table.CheckConstraint("ck_payout_positions_position", "position >= 1");
                    table.ForeignKey(
                        name: "fk_payout_positions_group_memberships_membership_id",
                        column: x => x.membership_id,
                        principalTable: "group_memberships",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payout_positions_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "scheme_rounds",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    round_number = table.Column<int>(type: "integer", nullable: false),
                    contribution_due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    payout_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scheme_rounds", x => x.id);
                    table.CheckConstraint("ck_scheme_rounds_dates", "payout_date >= contribution_due_date");
                    table.CheckConstraint("ck_scheme_rounds_number", "round_number >= 1");
                    table.CheckConstraint("ck_scheme_rounds_status", "status IN ('Scheduled', 'Completed')");
                    table.ForeignKey(
                        name: "fk_scheme_rounds_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "scheme_versions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<int>(type: "integer", nullable: false),
                    rules_json = table.Column<string>(type: "jsonb", nullable: false),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_scheme_versions", x => x.id);
                    table.CheckConstraint("ck_scheme_versions_version", "version >= 1");
                    table.ForeignKey(
                        name: "fk_scheme_versions_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_scheme_versions_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payouts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    round_id = table.Column<Guid>(type: "uuid", nullable: false),
                    recipient_membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    scheduled_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payouts", x => x.id);
                    table.CheckConstraint("ck_payouts_amount", "amount_minor >= 0");
                    table.CheckConstraint("ck_payouts_currency", "currency = 'NGN'");
                    table.CheckConstraint("ck_payouts_status", "status IN ('Scheduled')");
                    table.ForeignKey(
                        name: "fk_payouts_group_memberships_recipient_membership_id",
                        column: x => x.recipient_membership_id,
                        principalTable: "group_memberships",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payouts_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payouts_scheme_rounds_round_id",
                        column: x => x.round_id,
                        principalTable: "scheme_rounds",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_actor_id",
                table: "audit_logs",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_entity_type_entity_id",
                table: "audit_logs",
                columns: new[] { "entity_type", "entity_id" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_logs_group_id_created_at",
                table: "audit_logs",
                columns: new[] { "group_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_contact_verifications_user_id_created_at",
                table: "contact_verifications",
                columns: new[] { "user_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_contribution_obligations_due_date",
                table: "contribution_obligations",
                column: "due_date");

            migrationBuilder.CreateIndex(
                name: "ix_contribution_obligations_group_id_status",
                table: "contribution_obligations",
                columns: new[] { "group_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_contribution_obligations_membership_id_round_id",
                table: "contribution_obligations",
                columns: new[] { "membership_id", "round_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_contribution_obligations_round_id",
                table: "contribution_obligations",
                column: "round_id");

            migrationBuilder.CreateIndex(
                name: "ix_group_invitations_group_id",
                table: "group_invitations",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_group_invitations_token_hash",
                table: "group_invitations",
                column: "token_hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_group_memberships_accepted_scheme_version_id",
                table: "group_memberships",
                column: "accepted_scheme_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_group_memberships_group_id_join_sequence",
                table: "group_memberships",
                columns: new[] { "group_id", "join_sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_group_memberships_group_id_user_id",
                table: "group_memberships",
                columns: new[] { "group_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_group_memberships_joined_via_invitation_id",
                table: "group_memberships",
                column: "joined_via_invitation_id");

            migrationBuilder.CreateIndex(
                name: "ix_group_memberships_user_id",
                table: "group_memberships",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_groups_current_scheme_version_id",
                table: "groups",
                column: "current_scheme_version_id");

            migrationBuilder.CreateIndex(
                name: "ix_groups_organizer_id_status",
                table: "groups",
                columns: new[] { "organizer_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_payout_positions_group_id_position",
                table: "payout_positions",
                columns: new[] { "group_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payout_positions_membership_id",
                table: "payout_positions",
                column: "membership_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_payouts_group_id_scheduled_date",
                table: "payouts",
                columns: new[] { "group_id", "scheduled_date" });

            migrationBuilder.CreateIndex(
                name: "ix_payouts_recipient_membership_id",
                table: "payouts",
                column: "recipient_membership_id");

            migrationBuilder.CreateIndex(
                name: "ix_payouts_round_id",
                table: "payouts",
                column: "round_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_scheme_rounds_group_id_round_number",
                table: "scheme_rounds",
                columns: new[] { "group_id", "round_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_scheme_versions_created_by",
                table: "scheme_versions",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_scheme_versions_group_id_version",
                table: "scheme_versions",
                columns: new[] { "group_id", "version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_users_email",
                table: "users",
                column: "email",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_contribution_obligations_group_memberships_membership_id",
                table: "contribution_obligations",
                column: "membership_id",
                principalTable: "group_memberships",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_contribution_obligations_groups_group_id",
                table: "contribution_obligations",
                column: "group_id",
                principalTable: "groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_contribution_obligations_scheme_rounds_round_id",
                table: "contribution_obligations",
                column: "round_id",
                principalTable: "scheme_rounds",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_group_invitations_groups_group_id",
                table: "group_invitations",
                column: "group_id",
                principalTable: "groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_group_memberships_groups_group_id",
                table: "group_memberships",
                column: "group_id",
                principalTable: "groups",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_group_memberships_scheme_versions_accepted_scheme_version_id",
                table: "group_memberships",
                column: "accepted_scheme_version_id",
                principalTable: "scheme_versions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_groups_scheme_versions_current_scheme_version_id",
                table: "groups",
                column: "current_scheme_version_id",
                principalTable: "scheme_versions",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_groups_users_organizer_id",
                table: "groups");

            migrationBuilder.DropForeignKey(
                name: "fk_scheme_versions_users_created_by",
                table: "scheme_versions");

            migrationBuilder.DropForeignKey(
                name: "fk_scheme_versions_groups_group_id",
                table: "scheme_versions");

            migrationBuilder.DropTable(
                name: "audit_logs");

            migrationBuilder.DropTable(
                name: "contact_verifications");

            migrationBuilder.DropTable(
                name: "contribution_obligations");

            migrationBuilder.DropTable(
                name: "payout_positions");

            migrationBuilder.DropTable(
                name: "payouts");

            migrationBuilder.DropTable(
                name: "group_memberships");

            migrationBuilder.DropTable(
                name: "scheme_rounds");

            migrationBuilder.DropTable(
                name: "group_invitations");

            migrationBuilder.DropTable(
                name: "users");

            migrationBuilder.DropTable(
                name: "groups");

            migrationBuilder.DropTable(
                name: "scheme_versions");
        }
    }
}
