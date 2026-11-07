using AjoCircle.Domain.Audit;
using AjoCircle.Domain.Groups;
using AjoCircle.Domain.Identity;
using AjoCircle.Domain.Notifications;
using AjoCircle.Domain.Records;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace AjoCircle.Application;

public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<ContactVerification> ContactVerifications { get; }
    DbSet<Group> Groups { get; }
    DbSet<SchemeVersion> SchemeVersions { get; }
    DbSet<GroupMembership> GroupMemberships { get; }
    DbSet<GroupInvitation> GroupInvitations { get; }
    DbSet<PayoutPosition> PayoutPositions { get; }
    DbSet<AllocationShare> AllocationShares { get; }
    DbSet<SchemeRound> SchemeRounds { get; }
    DbSet<ContributionObligation> ContributionObligations { get; }
    DbSet<Payout> Payouts { get; }
    DbSet<ContributionRecord> ContributionRecords { get; }
    DbSet<PayoutRecord> PayoutRecords { get; }
    DbSet<RecordAmendment> RecordAmendments { get; }
    DbSet<Dispute> Disputes { get; }
    DbSet<DisputeMessage> DisputeMessages { get; }
    DbSet<Notification> Notifications { get; }
    DbSet<PasswordResetToken> PasswordResetTokens { get; }
    DbSet<AuditLog> AuditLogs { get; }

    Task<int> SaveChangesAsync(CancellationToken ct = default);
    Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken ct = default);

    /// <summary>Loads a group under a row lock (SELECT … FOR UPDATE) so capacity checks are race-free.</summary>
    Task<Group?> LockGroupAsync(Guid groupId, CancellationToken ct = default);

    /// <summary>Locks up to <paramref name="max"/> notifications due for email (FOR UPDATE SKIP LOCKED). Call inside a transaction.</summary>
    Task<List<Notification>> ClaimPendingNotificationsAsync(DateTimeOffset now, int max, CancellationToken ct = default);
}

/// <summary>The authenticated caller. Identity comes from the server-side session, never from the request body.</summary>
public interface ICurrentUser
{
    Guid? UserId { get; }
}

public interface IAppClock
{
    DateTimeOffset UtcNow { get; }
    /// <summary>Today's calendar date in Nigeria (Africa/Lagos, UTC+1).</summary>
    DateOnly LagosToday { get; }
}

public interface IPasswordHashing
{
    string Hash(User user, string password);
    bool Verify(User user, string password);
}

public sealed class NotFoundException(string message = "The requested resource was not found.") : Exception(message);
public sealed class ForbiddenException(string message = "You do not have permission to do that.") : Exception(message);
public sealed class UnauthenticatedException() : Exception("Sign in to continue.");
