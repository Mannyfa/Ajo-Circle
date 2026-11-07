using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AjoCircle.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class RecordsAndDisputes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_payouts_status",
                table: "payouts");

            migrationBuilder.CreateTable(
                name: "contribution_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    obligation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    paid_on = table.Column<DateOnly>(type: "date", nullable: false),
                    method = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    replaces_record_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    confirmed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reversed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reversed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reversal_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_contribution_records", x => x.id);
                    table.CheckConstraint("ck_contribution_records_amount", "amount_minor > 0");
                    table.CheckConstraint("ck_contribution_records_currency", "currency = 'NGN'");
                    table.CheckConstraint("ck_contribution_records_method", "method IN ('Cash', 'BankTransfer', 'PosOrMobileMoney', 'Other')");
                    table.CheckConstraint("ck_contribution_records_reversal", "(status = 'Reversed') = (reversed_at IS NOT NULL AND reversed_by IS NOT NULL AND reversal_reason IS NOT NULL)");
                    table.CheckConstraint("ck_contribution_records_status", "status IN ('RecordedByOrganizer', 'ConfirmedByMember', 'Disputed', 'Reversed')");
                    table.ForeignKey(
                        name: "fk_contribution_records_contribution_obligations_obligation_id",
                        column: x => x.obligation_id,
                        principalTable: "contribution_obligations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_contribution_records_contribution_records_replaces_record_id",
                        column: x => x.replaces_record_id,
                        principalTable: "contribution_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_contribution_records_group_memberships_subject_membership_id",
                        column: x => x.subject_membership_id,
                        principalTable: "group_memberships",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_contribution_records_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_contribution_records_users_confirmed_by",
                        column: x => x.confirmed_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_contribution_records_users_recorded_by",
                        column: x => x.recorded_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_contribution_records_users_reversed_by",
                        column: x => x.reversed_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "disputes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    reference = table.Column<string>(type: "character varying(12)", maxLength: 12, nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    record_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    raised_by = table.Column<Guid>(type: "uuid", nullable: false),
                    category = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    raised_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    resolution = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    resolution_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    resolved_by = table.Column<Guid>(type: "uuid", nullable: true),
                    resolved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    corrective_record_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_disputes", x => x.id);
                    table.CheckConstraint("ck_disputes_category", "category IN ('DidNotHappen', 'WrongAmount', 'WrongDate', 'Other')");
                    table.CheckConstraint("ck_disputes_record_type", "record_type IN ('ContributionRecord', 'PayoutRecord')");
                    table.CheckConstraint("ck_disputes_resolution", "resolution IS NULL OR resolution IN ('WithdrawnByMember', 'RecordCorrected', 'RecordReversed')");
                    table.CheckConstraint("ck_disputes_resolved_fields", "(status = 'Open' AND resolved_at IS NULL AND resolution IS NULL) OR (status = 'Resolved' AND resolved_at IS NOT NULL AND resolution IS NOT NULL AND resolved_by IS NOT NULL)");
                    table.CheckConstraint("ck_disputes_status", "status IN ('Open', 'Resolved')");
                    table.ForeignKey(
                        name: "fk_disputes_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_disputes_users_raised_by",
                        column: x => x.raised_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_disputes_users_resolved_by",
                        column: x => x.resolved_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "payout_records",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payout_id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    subject_membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    paid_on = table.Column<DateOnly>(type: "date", nullable: false),
                    method = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    evidence_reference = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    recorded_by = table.Column<Guid>(type: "uuid", nullable: false),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    replaces_record_id = table.Column<Guid>(type: "uuid", nullable: true),
                    status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    confirmed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reversed_by = table.Column<Guid>(type: "uuid", nullable: true),
                    reversed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reversal_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_payout_records", x => x.id);
                    table.CheckConstraint("ck_payout_records_amount", "amount_minor > 0");
                    table.CheckConstraint("ck_payout_records_currency", "currency = 'NGN'");
                    table.CheckConstraint("ck_payout_records_method", "method IN ('Cash', 'BankTransfer', 'PosOrMobileMoney', 'Other')");
                    table.CheckConstraint("ck_payout_records_reversal", "(status = 'Reversed') = (reversed_at IS NOT NULL AND reversed_by IS NOT NULL AND reversal_reason IS NOT NULL)");
                    table.CheckConstraint("ck_payout_records_status", "status IN ('RecordedByOrganizer', 'ConfirmedByMember', 'Disputed', 'Reversed')");
                    table.ForeignKey(
                        name: "fk_payout_records_group_memberships_subject_membership_id",
                        column: x => x.subject_membership_id,
                        principalTable: "group_memberships",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payout_records_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payout_records_payout_records_replaces_record_id",
                        column: x => x.replaces_record_id,
                        principalTable: "payout_records",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payout_records_payouts_payout_id",
                        column: x => x.payout_id,
                        principalTable: "payouts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payout_records_users_confirmed_by",
                        column: x => x.confirmed_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payout_records_users_recorded_by",
                        column: x => x.recorded_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_payout_records_users_reversed_by",
                        column: x => x.reversed_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "record_amendments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    record_type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    record_id = table.Column<Guid>(type: "uuid", nullable: false),
                    replacement_record_id = table.Column<Guid>(type: "uuid", nullable: true),
                    reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    old_values = table.Column<string>(type: "jsonb", nullable: false),
                    new_values = table.Column<string>(type: "jsonb", nullable: true),
                    created_by = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_record_amendments", x => x.id);
                    table.CheckConstraint("ck_record_amendments_record_type", "record_type IN ('ContributionRecord', 'PayoutRecord')");
                    table.ForeignKey(
                        name: "fk_record_amendments_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_record_amendments_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "dispute_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    dispute_id = table.Column<Guid>(type: "uuid", nullable: false),
                    author_id = table.Column<Guid>(type: "uuid", nullable: false),
                    body = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_dispute_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_dispute_messages_disputes_dispute_id",
                        column: x => x.dispute_id,
                        principalTable: "disputes",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_dispute_messages_users_author_id",
                        column: x => x.author_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_payouts_status",
                table: "payouts",
                sql: "status IN ('Scheduled', 'RecordedByOrganizer', 'ConfirmedByRecipient', 'Disputed')");

            migrationBuilder.CreateIndex(
                name: "ix_contribution_records_confirmed_by",
                table: "contribution_records",
                column: "confirmed_by");

            migrationBuilder.CreateIndex(
                name: "ix_contribution_records_group_id_status",
                table: "contribution_records",
                columns: new[] { "group_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_contribution_records_obligation_id_recorded_at",
                table: "contribution_records",
                columns: new[] { "obligation_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_contribution_records_recorded_by",
                table: "contribution_records",
                column: "recorded_by");

            migrationBuilder.CreateIndex(
                name: "ix_contribution_records_replaces_record_id",
                table: "contribution_records",
                column: "replaces_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_contribution_records_reversed_by",
                table: "contribution_records",
                column: "reversed_by");

            migrationBuilder.CreateIndex(
                name: "ix_contribution_records_subject_membership_id",
                table: "contribution_records",
                column: "subject_membership_id");

            migrationBuilder.CreateIndex(
                name: "ux_contribution_records_one_active",
                table: "contribution_records",
                column: "obligation_id",
                unique: true,
                filter: "status <> 'Reversed'");

            migrationBuilder.CreateIndex(
                name: "ix_dispute_messages_author_id",
                table: "dispute_messages",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_dispute_messages_dispute_id_created_at",
                table: "dispute_messages",
                columns: new[] { "dispute_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_disputes_group_id_status",
                table: "disputes",
                columns: new[] { "group_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_disputes_raised_by",
                table: "disputes",
                column: "raised_by");

            migrationBuilder.CreateIndex(
                name: "ix_disputes_reference",
                table: "disputes",
                column: "reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_disputes_resolved_by",
                table: "disputes",
                column: "resolved_by");

            migrationBuilder.CreateIndex(
                name: "ux_disputes_one_open_per_record",
                table: "disputes",
                column: "record_id",
                unique: true,
                filter: "status = 'Open'");

            migrationBuilder.CreateIndex(
                name: "ix_payout_records_confirmed_by",
                table: "payout_records",
                column: "confirmed_by");

            migrationBuilder.CreateIndex(
                name: "ix_payout_records_group_id_status",
                table: "payout_records",
                columns: new[] { "group_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_payout_records_payout_id_recorded_at",
                table: "payout_records",
                columns: new[] { "payout_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_payout_records_recorded_by",
                table: "payout_records",
                column: "recorded_by");

            migrationBuilder.CreateIndex(
                name: "ix_payout_records_replaces_record_id",
                table: "payout_records",
                column: "replaces_record_id");

            migrationBuilder.CreateIndex(
                name: "ix_payout_records_reversed_by",
                table: "payout_records",
                column: "reversed_by");

            migrationBuilder.CreateIndex(
                name: "ix_payout_records_subject_membership_id",
                table: "payout_records",
                column: "subject_membership_id");

            migrationBuilder.CreateIndex(
                name: "ux_payout_records_one_active",
                table: "payout_records",
                column: "payout_id",
                unique: true,
                filter: "status <> 'Reversed'");

            migrationBuilder.CreateIndex(
                name: "ix_record_amendments_created_by",
                table: "record_amendments",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_record_amendments_group_id",
                table: "record_amendments",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_record_amendments_record_type_record_id",
                table: "record_amendments",
                columns: new[] { "record_type", "record_id" });

            // Brief §11.4: records are append-only. Only the listed status columns may change, a record frozen in its
            // final state can't change at all, and nothing can be deleted. The application never relies on this alone.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION ajo_guard_status_only() RETURNS trigger AS $$
                DECLARE
                    mutable text[] := TG_ARGV[1:TG_NARGS - 1];
                BEGIN
                    IF TG_OP = 'DELETE' THEN
                        RAISE EXCEPTION 'ajo_append_only: DELETE on % is not permitted', TG_TABLE_NAME USING ERRCODE = 'restrict_violation';
                    END IF;
                    IF OLD.status = TG_ARGV[0] THEN
                        RAISE EXCEPTION 'ajo_append_only: % row is final', TG_TABLE_NAME USING ERRCODE = 'restrict_violation';
                    END IF;
                    IF (to_jsonb(NEW) - mutable) IS DISTINCT FROM (to_jsonb(OLD) - mutable) THEN
                        RAISE EXCEPTION 'ajo_append_only: only status fields of % may change', TG_TABLE_NAME USING ERRCODE = 'restrict_violation';
                    END IF;
                    RETURN NEW;
                END;
                $$ LANGUAGE plpgsql;

                CREATE TRIGGER contribution_records_guard BEFORE UPDATE OR DELETE ON contribution_records
                    FOR EACH ROW EXECUTE FUNCTION ajo_guard_status_only('Reversed', 'status', 'confirmed_by', 'confirmed_at', 'reversed_by', 'reversed_at', 'reversal_reason');
                CREATE TRIGGER payout_records_guard BEFORE UPDATE OR DELETE ON payout_records
                    FOR EACH ROW EXECUTE FUNCTION ajo_guard_status_only('Reversed', 'status', 'confirmed_by', 'confirmed_at', 'reversed_by', 'reversed_at', 'reversal_reason');
                CREATE TRIGGER disputes_guard BEFORE UPDATE OR DELETE ON disputes
                    FOR EACH ROW EXECUTE FUNCTION ajo_guard_status_only('Resolved', 'status', 'resolution', 'resolution_note', 'resolved_by', 'resolved_at', 'corrective_record_id');
                CREATE TRIGGER record_amendments_append_only BEFORE UPDATE OR DELETE ON record_amendments
                    FOR EACH ROW EXECUTE FUNCTION ajo_reject_change();
                CREATE TRIGGER dispute_messages_append_only BEFORE UPDATE OR DELETE ON dispute_messages
                    FOR EACH ROW EXECUTE FUNCTION ajo_reject_change();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS dispute_messages_append_only ON dispute_messages;
                DROP TRIGGER IF EXISTS record_amendments_append_only ON record_amendments;
                DROP TRIGGER IF EXISTS disputes_guard ON disputes;
                DROP TRIGGER IF EXISTS payout_records_guard ON payout_records;
                DROP TRIGGER IF EXISTS contribution_records_guard ON contribution_records;
                DROP FUNCTION IF EXISTS ajo_guard_status_only();
                """);
            migrationBuilder.DropTable(
                name: "contribution_records");

            migrationBuilder.DropTable(
                name: "dispute_messages");

            migrationBuilder.DropTable(
                name: "payout_records");

            migrationBuilder.DropTable(
                name: "record_amendments");

            migrationBuilder.DropTable(
                name: "disputes");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payouts_status",
                table: "payouts");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payouts_status",
                table: "payouts",
                sql: "status IN ('Scheduled')");
        }
    }
}
