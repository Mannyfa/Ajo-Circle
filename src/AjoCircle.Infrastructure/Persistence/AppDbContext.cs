using AjoCircle.Application;
using AjoCircle.Domain.Audit;
using AjoCircle.Domain.Groups;
using AjoCircle.Domain.Identity;
using AjoCircle.Domain.Notifications;
using AjoCircle.Domain.Records;
using Microsoft.EntityFrameworkCore;

using Microsoft.EntityFrameworkCore.Storage;

namespace AjoCircle.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<ContactVerification> ContactVerifications => Set<ContactVerification>();
    public DbSet<Group> Groups => Set<Group>();
    public DbSet<SchemeVersion> SchemeVersions => Set<SchemeVersion>();
    public DbSet<GroupMembership> GroupMemberships => Set<GroupMembership>();
    public DbSet<GroupInvitation> GroupInvitations => Set<GroupInvitation>();
    public DbSet<PayoutPosition> PayoutPositions => Set<PayoutPosition>();
    public DbSet<AllocationShare> AllocationShares => Set<AllocationShare>();
    public DbSet<SchemeRound> SchemeRounds => Set<SchemeRound>();
    public DbSet<ContributionObligation> ContributionObligations => Set<ContributionObligation>();
    public DbSet<Payout> Payouts => Set<Payout>();
    public DbSet<ContributionRecord> ContributionRecords => Set<ContributionRecord>();
    public DbSet<PayoutRecord> PayoutRecords => Set<PayoutRecord>();
    public DbSet<RecordAmendment> RecordAmendments => Set<RecordAmendment>();
    public DbSet<Dispute> Disputes => Set<Dispute>();
    public DbSet<DisputeMessage> DisputeMessages => Set<DisputeMessage>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default) => Database.BeginTransactionAsync(ct);

    public async Task<Group?> LockGroupAsync(Guid groupId, CancellationToken ct = default)
    {
        // xmin is a system column, so it must be selected explicitly for the concurrency token.
        var rows = await Groups.FromSql($"SELECT *, xmin FROM groups WHERE id = {groupId} FOR UPDATE").ToListAsync(ct);
        return rows.SingleOrDefault();
    }

    public Task<List<Notification>> ClaimPendingNotificationsAsync(DateTimeOffset now, int max, CancellationToken ct = default) =>
        Notifications.FromSql($"""
            SELECT * FROM notifications
            WHERE email_status = 'Pending' AND next_email_attempt_at <= {now}
            ORDER BY next_email_attempt_at
            LIMIT {max}
            FOR UPDATE SKIP LOCKED
            """).ToListAsync(ct);

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.ToTable("users");
            e.HasIndex(x => x.Email).IsUnique();
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.DisplayName).HasMaxLength(60);
            e.Property(x => x.PasswordHash).HasMaxLength(512);
            e.Property(x => x.SecurityStamp).HasMaxLength(64);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        });

        b.Entity<ContactVerification>(e =>
        {
            e.ToTable("contact_verifications");
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.CodeHash).HasMaxLength(64);
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
        });

        b.Entity<Group>(e =>
        {
            e.ToTable("groups", t =>
            {
                t.HasCheckConstraint("ck_groups_currency", "currency = 'NGN'");
                t.HasCheckConstraint("ck_groups_status", StatusCheck<GroupStatus>("status"));
            });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.OrganizerId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Name).HasMaxLength(80);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.SchemeType).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.Property(x => x.DraftRulesJson).HasColumnType("jsonb");
            e.Property(x => x.CancellationReason).HasMaxLength(500);
            e.Property(x => x.RowVersion).IsRowVersion(); // maps to PostgreSQL xmin
            e.HasIndex(x => new { x.OrganizerId, x.Status });
            e.HasOne<SchemeVersion>().WithMany().HasForeignKey(x => x.CurrentSchemeVersionId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<SchemeVersion>(e =>
        {
            e.ToTable("scheme_versions", t => t.HasCheckConstraint("ck_scheme_versions_version", "version >= 1"));
            e.HasOne<Group>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.RulesJson).HasColumnType("jsonb");
            e.HasIndex(x => new { x.GroupId, x.Version }).IsUnique();
        });

        b.Entity<GroupMembership>(e =>
        {
            e.ToTable("group_memberships", t =>
            {
                t.HasCheckConstraint("ck_group_memberships_status", StatusCheck<MembershipStatus>("status"));
                t.HasCheckConstraint("ck_group_memberships_join_sequence", "join_sequence >= 1");
            });
            e.HasOne<Group>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SchemeVersion>().WithMany().HasForeignKey(x => x.AcceptedSchemeVersionId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<GroupInvitation>().WithMany().HasForeignKey(x => x.JoinedViaInvitationId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ExitReason).HasMaxLength(500);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ExitedBy).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.GroupId, x.UserId }).IsUnique();
            e.HasIndex(x => new { x.GroupId, x.JoinSequence }).IsUnique();
            e.HasIndex(x => x.UserId);
        });

        b.Entity<GroupInvitation>(e =>
        {
            e.ToTable("group_invitations", t =>
            {
                t.HasCheckConstraint("ck_group_invitations_use_count", "use_count >= 0 AND (max_uses IS NULL OR use_count <= max_uses)");
            });
            e.HasOne<Group>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.GroupId);
        });

        b.Entity<PayoutPosition>(e =>
        {
            e.ToTable("payout_positions", t => t.HasCheckConstraint("ck_payout_positions_position", "position >= 1"));
            e.HasOne<Group>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<GroupMembership>().WithMany().HasForeignKey(x => x.MembershipId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.GroupId, x.Position }).IsUnique();
            e.HasIndex(x => x.MembershipId).IsUnique();
        });

        b.Entity<AllocationShare>(e =>
        {
            e.ToTable("allocation_shares", t => t.HasCheckConstraint("ck_allocation_shares_bp", "basis_points BETWEEN 0 AND 10000"));
            e.HasOne<Group>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<GroupMembership>().WithMany().HasForeignKey(x => x.MembershipId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.AssignedBy).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.MembershipId).IsUnique();
            e.HasIndex(x => x.GroupId);
        });

        b.Entity<SchemeRound>(e =>
        {
            e.ToTable("scheme_rounds", t =>
            {
                t.HasCheckConstraint("ck_scheme_rounds_number", "round_number >= 1");
                t.HasCheckConstraint("ck_scheme_rounds_dates", "payout_date >= contribution_due_date");
                t.HasCheckConstraint("ck_scheme_rounds_status", StatusCheck<RoundStatus>("status"));
            });
            e.HasOne<Group>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.GroupId, x.RoundNumber }).IsUnique();
        });

        b.Entity<ContributionObligation>(e =>
        {
            e.ToTable("contribution_obligations", t =>
            {
                t.HasCheckConstraint("ck_contribution_obligations_amount", "amount_minor >= 0");
                t.HasCheckConstraint("ck_contribution_obligations_currency", "currency = 'NGN'");
                t.HasCheckConstraint("ck_contribution_obligations_status", StatusCheck<ObligationStatus>("status"));
            });
            e.HasOne<Group>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<GroupMembership>().WithMany().HasForeignKey(x => x.MembershipId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SchemeRound>().WithMany().HasForeignKey(x => x.RoundId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            e.HasIndex(x => new { x.MembershipId, x.RoundId }).IsUnique();
            e.HasIndex(x => new { x.GroupId, x.Status });
            e.HasIndex(x => x.DueDate);
            e.HasIndex(x => x.RoundId);
        });

        b.Entity<Payout>(e =>
        {
            e.ToTable("payouts", t =>
            {
                t.HasCheckConstraint("ck_payouts_amount", "amount_minor >= 0");
                t.HasCheckConstraint("ck_payouts_currency", "currency = 'NGN'");
                t.HasCheckConstraint("ck_payouts_status", StatusCheck<PayoutStatus>("status"));
                t.HasCheckConstraint("ck_payouts_kind", StatusCheck<PayoutKind>("kind"));
                // A rotating payout always belongs to a round; money owed back never does.
                t.HasCheckConstraint("ck_payouts_round", "(kind = 'RotatingPayout') = (round_id IS NOT NULL)");
            });
            e.HasOne<Group>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SchemeRound>().WithMany().HasForeignKey(x => x.RoundId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<GroupMembership>().WithMany().HasForeignKey(x => x.RecipientMembershipId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(30).HasDefaultValue(PayoutKind.RotatingPayout).HasSentinel((PayoutKind)(-1));
            e.HasIndex(x => x.RoundId).IsUnique(); // standard rotating model: one payout per round (NULLs don't collide)
            // At most one release / exit refund / cancellation refund per member, so a retried command can't owe twice.
            e.HasIndex(x => new { x.RecipientMembershipId, x.Kind }).IsUnique().HasFilter("kind <> 'RotatingPayout'").HasDatabaseName("ux_payouts_one_owed_back_per_kind");
            e.HasIndex(x => new { x.GroupId, x.ScheduledDate });
            e.HasIndex(x => x.RecipientMembershipId);
        });

        b.Entity<ContributionRecord>(e =>
        {
            e.ToTable("contribution_records", t => ExternalRecordChecks(t, "contribution_records"));
            ConfigureExternalRecord(e);
            e.HasOne<ContributionObligation>().WithMany().HasForeignKey(x => x.ObligationId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<ContributionRecord>().WithMany().HasForeignKey(x => x.ReplacesRecordId).OnDelete(DeleteBehavior.Restrict);
            // At most one current (non-reversed) record per obligation: a double submit can't create two.
            e.HasIndex(x => x.ObligationId).IsUnique().HasFilter("status <> 'Reversed'").HasDatabaseName("ux_contribution_records_one_active");
            e.HasIndex(x => new { x.ObligationId, x.RecordedAt });
        });

        b.Entity<PayoutRecord>(e =>
        {
            e.ToTable("payout_records", t => ExternalRecordChecks(t, "payout_records"));
            ConfigureExternalRecord(e);
            e.HasOne<Payout>().WithMany().HasForeignKey(x => x.PayoutId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<PayoutRecord>().WithMany().HasForeignKey(x => x.ReplacesRecordId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => x.PayoutId).IsUnique().HasFilter("status <> 'Reversed'").HasDatabaseName("ux_payout_records_one_active");
            e.HasIndex(x => new { x.PayoutId, x.RecordedAt });
        });

        b.Entity<RecordAmendment>(e =>
        {
            e.ToTable("record_amendments", t => t.HasCheckConstraint("ck_record_amendments_record_type", StatusCheck<RecordKind>("record_type")));
            e.HasOne<Group>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedBy).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.RecordType).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.Property(x => x.OldValues).HasColumnType("jsonb");
            e.Property(x => x.NewValues).HasColumnType("jsonb");
            e.HasIndex(x => new { x.RecordType, x.RecordId });
            e.HasIndex(x => x.GroupId);
        });

        b.Entity<Dispute>(e =>
        {
            e.ToTable("disputes", t =>
            {
                t.HasCheckConstraint("ck_disputes_status", StatusCheck<DisputeStatus>("status"));
                t.HasCheckConstraint("ck_disputes_record_type", StatusCheck<RecordKind>("record_type"));
                t.HasCheckConstraint("ck_disputes_category", StatusCheck<DisputeCategory>("category"));
                t.HasCheckConstraint("ck_disputes_resolution", $"resolution IS NULL OR {StatusCheck<DisputeResolution>("resolution")}");
                t.HasCheckConstraint("ck_disputes_resolved_fields",
                    "(status = 'Open' AND resolved_at IS NULL AND resolution IS NULL) OR (status = 'Resolved' AND resolved_at IS NOT NULL AND resolution IS NOT NULL AND resolved_by IS NOT NULL)");
            });
            e.HasOne<Group>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.RaisedBy).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.ResolvedBy).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Reference).HasMaxLength(12);
            e.Property(x => x.RecordType).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Category).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Resolution).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.Description).HasMaxLength(Dispute.MaxDescription);
            e.Property(x => x.ResolutionNote).HasMaxLength(500);
            e.HasIndex(x => x.Reference).IsUnique();
            e.HasIndex(x => x.RecordId).IsUnique().HasFilter("status = 'Open'").HasDatabaseName("ux_disputes_one_open_per_record");
            e.HasIndex(x => new { x.GroupId, x.Status });
            e.HasIndex(x => x.RaisedBy);
        });

        b.Entity<DisputeMessage>(e =>
        {
            e.ToTable("dispute_messages");
            e.HasOne<Dispute>().WithMany().HasForeignKey(x => x.DisputeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Body).HasMaxLength(DisputeMessage.MaxLength);
            e.HasIndex(x => new { x.DisputeId, x.CreatedAt });
        });

        b.Entity<Notification>(e =>
        {
            e.ToTable("notifications", t =>
            {
                t.HasCheckConstraint("ck_notifications_type", StatusCheck<NotificationType>("type"));
                t.HasCheckConstraint("ck_notifications_email_status", StatusCheck<EmailDelivery>("email_status"));
                t.HasCheckConstraint("ck_notifications_attempts", "email_attempts >= 0");
            });
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<Group>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(40);
            e.Property(x => x.EmailStatus).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Title).HasMaxLength(Notification.MaxTitle);
            e.Property(x => x.Body).HasMaxLength(Notification.MaxBody);
            e.Property(x => x.LinkPath).HasMaxLength(300);
            e.Property(x => x.DedupeKey).HasMaxLength(200);
            e.Property(x => x.EmailLastError).HasMaxLength(300);
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
            e.HasIndex(x => new { x.UserId, x.DedupeKey }).IsUnique().HasFilter("dedupe_key IS NOT NULL");
            e.HasIndex(x => x.NextEmailAttemptAt).HasFilter("email_status = 'Pending'");
        });

        b.Entity<PasswordResetToken>(e =>
        {
            e.ToTable("password_reset_tokens");
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.TokenHash).HasMaxLength(64);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => new { x.UserId, x.CreatedAt });
        });

        b.Entity<AuditLog>(e =>
        {
            e.ToTable("audit_logs");
            e.Property(x => x.Action).HasMaxLength(80);
            e.Property(x => x.EntityType).HasMaxLength(40);
            e.Property(x => x.Metadata).HasColumnType("jsonb");
            e.HasIndex(x => new { x.GroupId, x.CreatedAt });
            e.HasIndex(x => new { x.EntityType, x.EntityId });
            e.HasIndex(x => x.ActorId);
        });
    }

    private static void ConfigureExternalRecord<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> e) where T : ExternalRecord
    {
        e.HasOne<Group>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<GroupMembership>().WithMany().HasForeignKey(x => x.SubjectMembershipId).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<User>().WithMany().HasForeignKey(x => x.RecordedBy).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<User>().WithMany().HasForeignKey(x => x.ConfirmedBy).OnDelete(DeleteBehavior.Restrict);
        e.HasOne<User>().WithMany().HasForeignKey(x => x.ReversedBy).OnDelete(DeleteBehavior.Restrict);
        e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
        e.Property(x => x.Method).HasConversion<string>().HasMaxLength(30);
        e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
        e.Property(x => x.EvidenceReference).HasMaxLength(ReportedPayment.MaxEvidenceLength);
        e.Property(x => x.Note).HasMaxLength(ReportedPayment.MaxNoteLength);
        e.Property(x => x.ReversalReason).HasMaxLength(500);
        e.HasIndex(x => new { x.GroupId, x.Status });
        e.HasIndex(x => x.SubjectMembershipId);
    }

    private static void ExternalRecordChecks<T>(Microsoft.EntityFrameworkCore.Metadata.Builders.TableBuilder<T> t, string table) where T : class
    {
        t.HasCheckConstraint($"ck_{table}_amount", "amount_minor > 0");
        t.HasCheckConstraint($"ck_{table}_currency", "currency = 'NGN'");
        t.HasCheckConstraint($"ck_{table}_status", StatusCheck<RecordStatus>("status"));
        t.HasCheckConstraint($"ck_{table}_method", StatusCheck<PaymentMethod>("method"));
        t.HasCheckConstraint($"ck_{table}_reversal",
            "(status = 'Reversed') = (reversed_at IS NOT NULL AND reversed_by IS NOT NULL AND reversal_reason IS NOT NULL)");
    }

    private static string StatusCheck<TEnum>(string column) where TEnum : struct, Enum =>
        $"{column} IN ({string.Join(", ", Enum.GetNames<TEnum>().Select(n => $"'{n}'"))})";
}
