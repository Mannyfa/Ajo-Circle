using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AjoCircle.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OperationsAndIdentity : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_notifications_type",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "ux_disputes_one_open_per_record",
                table: "disputes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_disputes_resolution",
                table: "disputes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_disputes_resolved_fields",
                table: "disputes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_disputes_status",
                table: "disputes");

            migrationBuilder.AddColumn<string>(
                name: "identity_level",
                table: "users",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "None");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "identity_verified_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "is_operations",
                table: "users",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "mfa_enabled_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "mfa_last_step",
                table: "users",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "mfa_secret_protected",
                table: "users",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "phone_number",
                table: "users",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "phone_verified_at",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "decided_at",
                table: "disputes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "decided_by",
                table: "disputes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "decision",
                table: "disputes",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "decision_note",
                table: "disputes",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "escalated_at",
                table: "disputes",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "escalated_by",
                table: "disputes",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "channel",
                table: "contact_verifications",
                type: "character varying(10)",
                maxLength: 10,
                nullable: false,
                defaultValue: "Email");

            migrationBuilder.AddColumn<string>(
                name: "target",
                table: "contact_verifications",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "identity_submissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    id_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    id_number_protected = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    id_number_last4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: false),
                    bvn_protected = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    bvn_last4 = table.Column<string>(type: "character varying(4)", maxLength: 4, nullable: true),
                    phone_number = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    provider_reference = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    decision_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    data_purged_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identity_submissions", x => x.id);
                    table.CheckConstraint("ck_identity_submissions_bvn", "level = 'Organizer' OR (bvn_protected IS NULL AND bvn_last4 IS NULL)");
                    table.CheckConstraint("ck_identity_submissions_level", "level IN ('Member', 'Organizer')");
                    table.CheckConstraint("ck_identity_submissions_status", "status IN ('Pending', 'Approved', 'Rejected')");
                    table.ForeignKey(
                        name: "fk_identity_submissions_users_reviewed_by",
                        column: x => x.reviewed_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_identity_submissions_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "identity_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    content_type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    encrypted_content = table.Column<byte[]>(type: "bytea", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_identity_documents", x => x.id);
                    table.CheckConstraint("ck_identity_documents_kind", "kind IN ('IdFront', 'IdBack', 'PassportPhoto')");
                    table.ForeignKey(
                        name: "fk_identity_documents_identity_submissions_submission_id",
                        column: x => x.submission_id,
                        principalTable: "identity_submissions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_users_phone_number",
                table: "users",
                column: "phone_number",
                unique: true,
                filter: "phone_number IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notifications_type",
                table: "notifications",
                sql: "type IN ('MemberJoined', 'GroupReadyToStart', 'GroupStarted', 'GroupCancelled', 'ContributionRecorded', 'ContributionCorrected', 'PayoutRecorded', 'PayoutCorrected', 'DisputeRaised', 'DisputeReply', 'DisputeResolved', 'ContributionDueSoon', 'ContributionOverdue', 'PayoutApproaching', 'MemberExited', 'SavingsCompleted', 'IdentityApproved', 'IdentityRejected', 'DisputeEscalated', 'DisputeDecided')");

            migrationBuilder.CreateIndex(
                name: "ix_disputes_decided_by",
                table: "disputes",
                column: "decided_by");

            migrationBuilder.CreateIndex(
                name: "ix_disputes_escalated_by",
                table: "disputes",
                column: "escalated_by");

            migrationBuilder.CreateIndex(
                name: "ix_disputes_status_escalated_at",
                table: "disputes",
                columns: new[] { "status", "escalated_at" });

            migrationBuilder.CreateIndex(
                name: "ux_disputes_one_open_per_record",
                table: "disputes",
                column: "record_id",
                unique: true,
                filter: "status <> 'Resolved'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_disputes_decision",
                table: "disputes",
                sql: "decision IS NULL OR (decision IN ('RecordUpheld', 'CorrectionRequired') AND decided_by IS NOT NULL AND decided_at IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_disputes_escalated",
                table: "disputes",
                sql: "status <> 'Escalated' OR escalated_at IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_disputes_resolution",
                table: "disputes",
                sql: "resolution IS NULL OR resolution IN ('WithdrawnByMember', 'RecordCorrected', 'RecordReversed', 'UpheldByOperations')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_disputes_resolved_fields",
                table: "disputes",
                sql: "(status <> 'Resolved' AND resolved_at IS NULL AND resolution IS NULL) OR (status = 'Resolved' AND resolved_at IS NOT NULL AND resolution IS NOT NULL AND resolved_by IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_disputes_status",
                table: "disputes",
                sql: "status IN ('Open', 'Escalated', 'Resolved')");

            migrationBuilder.CreateIndex(
                name: "ix_identity_documents_submission_id_kind",
                table: "identity_documents",
                columns: new[] { "submission_id", "kind" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_identity_submissions_reviewed_by",
                table: "identity_submissions",
                column: "reviewed_by");

            migrationBuilder.CreateIndex(
                name: "ix_identity_submissions_status_submitted_at",
                table: "identity_submissions",
                columns: new[] { "status", "submitted_at" });

            migrationBuilder.CreateIndex(
                name: "ux_identity_submissions_one_pending",
                table: "identity_submissions",
                column: "user_id",
                unique: true,
                filter: "status = 'Pending'");

            migrationBuilder.AddForeignKey(
                name: "fk_disputes_users_decided_by",
                table: "disputes",
                column: "decided_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_disputes_users_escalated_by",
                table: "disputes",
                column: "escalated_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);

            // Disputes may now also move through escalation and an operations decision; still frozen once resolved.
            // Identity data can't be deleted; a document can only change by having its content wiped at retention.
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS disputes_guard ON disputes;
                CREATE TRIGGER disputes_guard BEFORE UPDATE OR DELETE ON disputes
                    FOR EACH ROW EXECUTE FUNCTION ajo_guard_status_only('Resolved', 'status', 'resolution', 'resolution_note', 'resolved_by', 'resolved_at',
                        'corrective_record_id', 'escalated_at', 'escalated_by', 'decision', 'decision_note', 'decided_by', 'decided_at');

                CREATE TRIGGER identity_submissions_guard BEFORE UPDATE OR DELETE ON identity_submissions
                    FOR EACH ROW EXECUTE FUNCTION ajo_guard_status_only('__never__', 'status', 'reviewed_by', 'reviewed_at', 'decision_note',
                        'data_purged_at', 'id_number_protected', 'bvn_protected');

                CREATE OR REPLACE FUNCTION ajo_identity_document_guard() RETURNS trigger AS $$
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'ajo_append_only: DELETE on identity_documents is not permitted' USING ERRCODE = 'restrict_violation';
                    END IF;
                    IF NEW.encrypted_content IS NOT NULL OR (to_jsonb(NEW) - 'encrypted_content') IS DISTINCT FROM (to_jsonb(OLD) - 'encrypted_content') THEN
                        RAISE EXCEPTION 'ajo_append_only: identity documents can only be purged' USING ERRCODE = 'restrict_violation';
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;
                CREATE TRIGGER identity_documents_guard BEFORE UPDATE OR DELETE ON identity_documents
                    FOR EACH ROW EXECUTE FUNCTION ajo_identity_document_guard();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS identity_documents_guard ON identity_documents;
                DROP FUNCTION IF EXISTS ajo_identity_document_guard();
                DROP TRIGGER IF EXISTS identity_submissions_guard ON identity_submissions;
                DROP TRIGGER IF EXISTS disputes_guard ON disputes;
                CREATE TRIGGER disputes_guard BEFORE UPDATE OR DELETE ON disputes
                    FOR EACH ROW EXECUTE FUNCTION ajo_guard_status_only('Resolved', 'status', 'resolution', 'resolution_note', 'resolved_by', 'resolved_at', 'corrective_record_id');
                """);
            migrationBuilder.DropForeignKey(
                name: "fk_disputes_users_decided_by",
                table: "disputes");

            migrationBuilder.DropForeignKey(
                name: "fk_disputes_users_escalated_by",
                table: "disputes");

            migrationBuilder.DropTable(
                name: "identity_documents");

            migrationBuilder.DropTable(
                name: "identity_submissions");

            migrationBuilder.DropIndex(
                name: "ix_users_phone_number",
                table: "users");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notifications_type",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "ix_disputes_decided_by",
                table: "disputes");

            migrationBuilder.DropIndex(
                name: "ix_disputes_escalated_by",
                table: "disputes");

            migrationBuilder.DropIndex(
                name: "ix_disputes_status_escalated_at",
                table: "disputes");

            migrationBuilder.DropIndex(
                name: "ux_disputes_one_open_per_record",
                table: "disputes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_disputes_decision",
                table: "disputes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_disputes_escalated",
                table: "disputes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_disputes_resolution",
                table: "disputes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_disputes_resolved_fields",
                table: "disputes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_disputes_status",
                table: "disputes");

            migrationBuilder.DropColumn(
                name: "identity_level",
                table: "users");

            migrationBuilder.DropColumn(
                name: "identity_verified_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "is_operations",
                table: "users");

            migrationBuilder.DropColumn(
                name: "mfa_enabled_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "mfa_last_step",
                table: "users");

            migrationBuilder.DropColumn(
                name: "mfa_secret_protected",
                table: "users");

            migrationBuilder.DropColumn(
                name: "phone_number",
                table: "users");

            migrationBuilder.DropColumn(
                name: "phone_verified_at",
                table: "users");

            migrationBuilder.DropColumn(
                name: "decided_at",
                table: "disputes");

            migrationBuilder.DropColumn(
                name: "decided_by",
                table: "disputes");

            migrationBuilder.DropColumn(
                name: "decision",
                table: "disputes");

            migrationBuilder.DropColumn(
                name: "decision_note",
                table: "disputes");

            migrationBuilder.DropColumn(
                name: "escalated_at",
                table: "disputes");

            migrationBuilder.DropColumn(
                name: "escalated_by",
                table: "disputes");

            migrationBuilder.DropColumn(
                name: "channel",
                table: "contact_verifications");

            migrationBuilder.DropColumn(
                name: "target",
                table: "contact_verifications");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notifications_type",
                table: "notifications",
                sql: "type IN ('MemberJoined', 'GroupReadyToStart', 'GroupStarted', 'GroupCancelled', 'ContributionRecorded', 'ContributionCorrected', 'PayoutRecorded', 'PayoutCorrected', 'DisputeRaised', 'DisputeReply', 'DisputeResolved', 'ContributionDueSoon', 'ContributionOverdue', 'PayoutApproaching', 'MemberExited', 'SavingsCompleted')");

            migrationBuilder.CreateIndex(
                name: "ux_disputes_one_open_per_record",
                table: "disputes",
                column: "record_id",
                unique: true,
                filter: "status = 'Open'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_disputes_resolution",
                table: "disputes",
                sql: "resolution IS NULL OR resolution IN ('WithdrawnByMember', 'RecordCorrected', 'RecordReversed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_disputes_resolved_fields",
                table: "disputes",
                sql: "(status = 'Open' AND resolved_at IS NULL AND resolution IS NULL) OR (status = 'Resolved' AND resolved_at IS NOT NULL AND resolution IS NOT NULL AND resolved_by IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_disputes_status",
                table: "disputes",
                sql: "status IN ('Open', 'Resolved')");
        }
    }
}
