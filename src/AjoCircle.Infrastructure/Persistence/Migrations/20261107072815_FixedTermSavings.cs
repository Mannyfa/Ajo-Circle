using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AjoCircle.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class FixedTermSavings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_notifications_type",
                table: "notifications");

            migrationBuilder.DropCheckConstraint(
                name: "ck_group_memberships_status",
                table: "group_memberships");

            migrationBuilder.DropCheckConstraint(
                name: "ck_contribution_obligations_status",
                table: "contribution_obligations");

            migrationBuilder.AlterColumn<Guid>(
                name: "round_id",
                table: "payouts",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "kind",
                table: "payouts",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "RotatingPayout");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "completed_at",
                table: "groups",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "exit_reason",
                table: "group_memberships",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "exited_at",
                table: "group_memberships",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "exited_by",
                table: "group_memberships",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "allocation_shares",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    group_id = table.Column<Guid>(type: "uuid", nullable: false),
                    membership_id = table.Column<Guid>(type: "uuid", nullable: false),
                    basis_points = table.Column<int>(type: "integer", nullable: false),
                    assigned_by = table.Column<Guid>(type: "uuid", nullable: false),
                    assigned_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_allocation_shares", x => x.id);
                    table.CheckConstraint("ck_allocation_shares_bp", "basis_points BETWEEN 0 AND 10000");
                    table.ForeignKey(
                        name: "fk_allocation_shares_group_memberships_membership_id",
                        column: x => x.membership_id,
                        principalTable: "group_memberships",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_allocation_shares_groups_group_id",
                        column: x => x.group_id,
                        principalTable: "groups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_allocation_shares_users_assigned_by",
                        column: x => x.assigned_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ux_payouts_one_owed_back_per_kind",
                table: "payouts",
                columns: new[] { "recipient_membership_id", "kind" },
                unique: true,
                filter: "kind <> 'RotatingPayout'");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payouts_kind",
                table: "payouts",
                sql: "kind IN ('RotatingPayout', 'Release', 'ExitRefund', 'CancellationRefund')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_payouts_round",
                table: "payouts",
                sql: "(kind = 'RotatingPayout') = (round_id IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "ck_notifications_type",
                table: "notifications",
                sql: "type IN ('MemberJoined', 'GroupReadyToStart', 'GroupStarted', 'GroupCancelled', 'ContributionRecorded', 'ContributionCorrected', 'PayoutRecorded', 'PayoutCorrected', 'DisputeRaised', 'DisputeReply', 'DisputeResolved', 'ContributionDueSoon', 'ContributionOverdue', 'PayoutApproaching', 'MemberExited', 'SavingsCompleted')");

            migrationBuilder.CreateIndex(
                name: "ix_group_memberships_exited_by",
                table: "group_memberships",
                column: "exited_by");

            migrationBuilder.AddCheckConstraint(
                name: "ck_group_memberships_status",
                table: "group_memberships",
                sql: "status IN ('Active', 'Removed', 'Exited')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_contribution_obligations_status",
                table: "contribution_obligations",
                sql: "status IN ('Due', 'SubmittedForReview', 'RecordedByOrganizer', 'ConfirmedByMember', 'VerifiedByProvider', 'Disputed', 'Reversed', 'Cancelled')");

            migrationBuilder.CreateIndex(
                name: "ix_allocation_shares_assigned_by",
                table: "allocation_shares",
                column: "assigned_by");

            migrationBuilder.CreateIndex(
                name: "ix_allocation_shares_group_id",
                table: "allocation_shares",
                column: "group_id");

            migrationBuilder.CreateIndex(
                name: "ix_allocation_shares_membership_id",
                table: "allocation_shares",
                column: "membership_id",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "fk_group_memberships_users_exited_by",
                table: "group_memberships",
                column: "exited_by",
                principalTable: "users",
                principalColumn: "id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_group_memberships_users_exited_by",
                table: "group_memberships");

            migrationBuilder.DropTable(
                name: "allocation_shares");

            migrationBuilder.DropIndex(
                name: "ux_payouts_one_owed_back_per_kind",
                table: "payouts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payouts_kind",
                table: "payouts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_payouts_round",
                table: "payouts");

            migrationBuilder.DropCheckConstraint(
                name: "ck_notifications_type",
                table: "notifications");

            migrationBuilder.DropIndex(
                name: "ix_group_memberships_exited_by",
                table: "group_memberships");

            migrationBuilder.DropCheckConstraint(
                name: "ck_group_memberships_status",
                table: "group_memberships");

            migrationBuilder.DropCheckConstraint(
                name: "ck_contribution_obligations_status",
                table: "contribution_obligations");

            migrationBuilder.DropColumn(
                name: "kind",
                table: "payouts");

            migrationBuilder.DropColumn(
                name: "completed_at",
                table: "groups");

            migrationBuilder.DropColumn(
                name: "exit_reason",
                table: "group_memberships");

            migrationBuilder.DropColumn(
                name: "exited_at",
                table: "group_memberships");

            migrationBuilder.DropColumn(
                name: "exited_by",
                table: "group_memberships");

            migrationBuilder.AlterColumn<Guid>(
                name: "round_id",
                table: "payouts",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_notifications_type",
                table: "notifications",
                sql: "type IN ('MemberJoined', 'GroupReadyToStart', 'GroupStarted', 'GroupCancelled', 'ContributionRecorded', 'ContributionCorrected', 'PayoutRecorded', 'PayoutCorrected', 'DisputeRaised', 'DisputeReply', 'DisputeResolved', 'ContributionDueSoon', 'ContributionOverdue', 'PayoutApproaching')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_group_memberships_status",
                table: "group_memberships",
                sql: "status IN ('Active', 'Removed')");

            migrationBuilder.AddCheckConstraint(
                name: "ck_contribution_obligations_status",
                table: "contribution_obligations",
                sql: "status IN ('Due', 'SubmittedForReview', 'RecordedByOrganizer', 'ConfirmedByMember', 'VerifiedByProvider', 'Disputed', 'Reversed')");
        }
    }
}
