using AjoCircle.Application;
using AjoCircle.Domain.Audit;
using AjoCircle.Domain.Groups;
using AjoCircle.Domain.Identity;
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
    public DbSet<SchemeRound> SchemeRounds => Set<SchemeRound>();
    public DbSet<ContributionObligation> ContributionObligations => Set<ContributionObligation>();
    public DbSet<Payout> Payouts => Set<Payout>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default) => Database.BeginTransactionAsync(ct);

    public async Task<Group?> LockGroupAsync(Guid groupId, CancellationToken ct = default)
    {
        // xmin is a system column, so it must be selected explicitly for the concurrency token.
        var rows = await Groups.FromSql($"SELECT *, xmin FROM groups WHERE id = {groupId} FOR UPDATE").ToListAsync(ct);
        return rows.SingleOrDefault();
    }

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
            });
            e.HasOne<Group>().WithMany().HasForeignKey(x => x.GroupId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<SchemeRound>().WithMany().HasForeignKey(x => x.RoundId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<GroupMembership>().WithMany().HasForeignKey(x => x.RecipientMembershipId).OnDelete(DeleteBehavior.Restrict);
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(30);
            e.HasIndex(x => x.RoundId).IsUnique(); // standard rotating model: one payout per round
            e.HasIndex(x => new { x.GroupId, x.ScheduledDate });
            e.HasIndex(x => x.RecipientMembershipId);
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

    private static string StatusCheck<TEnum>(string column) where TEnum : struct, Enum =>
        $"{column} IN ({string.Join(", ", Enum.GetNames<TEnum>().Select(n => $"'{n}'"))})";
}
