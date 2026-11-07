using System.Security.Cryptography;
using System.Text;
using AjoCircle.Domain.Common;

namespace AjoCircle.Domain.Identity;

public enum IdDocumentType { NationalId, InternationalPassport, DriversLicence, VotersCard }
public enum IdentitySubmissionStatus { Pending, Approved, Rejected }
public enum IdentityDocumentKind { IdFront, IdBack, PassportPhoto }

/// <summary>A file the user uploaded, already checked and encrypted by the application layer.</summary>
public sealed record UploadedDocument(IdentityDocumentKind Kind, string ContentType, byte[] Content);

/// <summary>
/// A request to have one's identity checked (owner decision 7), reviewed by operations. ID number and BVN are stored
/// encrypted; only their last 4 digits are kept in the clear. Documents are purged after the decision plus a retention
/// period (brief §11.2: minimise sensitive data). <see cref="Provider"/> is where a licensed verification provider's
/// reference will go once one is chosen; today every check is a manual review.
/// </summary>
public sealed class IdentitySubmission
{
    public const long MaxFileBytes = 5 * 1024 * 1024;

    private IdentitySubmission() { }
    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public IdentityLevel Level { get; private set; }
    public IdDocumentType IdType { get; private set; }
    public string? IdNumberProtected { get; private set; }
    public string IdNumberLast4 { get; private set; } = "";
    public string? BvnProtected { get; private set; }
    public string? BvnLast4 { get; private set; }
    /// <summary>E.164 phone that was verified when the user submitted.</summary>
    public string PhoneNumber { get; private set; } = "";
    public string Provider { get; private set; } = "manual_review";
    public string? ProviderReference { get; private set; }

    public IdentitySubmissionStatus Status { get; private set; }
    public DateTimeOffset SubmittedAt { get; private set; }
    public Guid? ReviewedBy { get; private set; }
    public DateTimeOffset? ReviewedAt { get; private set; }
    public string? DecisionNote { get; private set; }
    public DateTimeOffset? DataPurgedAt { get; private set; }

    /// <summary>
    /// Validates what the level requires. <paramref name="protect"/> encrypts identifiers before they reach the entity.
    /// </summary>
    public static IdentitySubmission Create(User user, IdentityLevel level, IdDocumentType idType, string? idNumber, string? bvn,
        IReadOnlyCollection<UploadedDocument> documents, Func<string, string> protect, DateTimeOffset now)
    {
        var errors = new Dictionary<string, List<string>>();
        void Add(string k, string m) => (errors.TryGetValue(k, out var l) ? l : errors[k] = []).Add(m);

        if (!user.IsContactVerified) Add("email", "Verify your email address first.");
        if (!user.IsPhoneVerified) Add("phone", "Verify your phone number first.");
        if (level is not (IdentityLevel.Member or IdentityLevel.Organizer)) Add("level", "Choose member or organizer verification.");
        if (user.HasIdentity(level) && level != IdentityLevel.None) Add("level", "You're already verified at this level.");
        if (!Enum.IsDefined(idType)) Add("idType", "Choose the type of ID document.");

        var id = Digits(idNumber, keepLetters: idType != IdDocumentType.NationalId);
        var idOk = idType switch
        {
            IdDocumentType.NationalId => id.Length == 11 && id.All(char.IsAsciiDigit),
            _ => id.Length is >= 6 and <= 20 && id.All(char.IsAsciiLetterOrDigit),
        };
        if (!idOk) Add("idNumber", idType == IdDocumentType.NationalId ? "Enter the 11-digit NIN." : "Enter the document number (6–20 letters or digits).");

        string? bvnDigits = null;
        if (level == IdentityLevel.Organizer)
        {
            bvnDigits = Digits(bvn, keepLetters: false);
            if (bvnDigits.Length != 11) Add("bvn", "Enter your 11-digit BVN.");
        }
        else if (!string.IsNullOrWhiteSpace(bvn)) Add("bvn", "Members don't need to give a BVN, so it isn't collected."); // data minimisation

        var kinds = documents.Select(d => d.Kind).ToList();
        if (kinds.Count != kinds.Distinct().Count()) Add("documents", "Upload each document once.");
        if (!kinds.Contains(IdentityDocumentKind.IdFront)) Add("idFront", "Upload a photo or scan of your ID document.");
        if (level == IdentityLevel.Organizer && !kinds.Contains(IdentityDocumentKind.PassportPhoto)) Add("passportPhoto", "Upload a passport photograph.");
        if (level == IdentityLevel.Member && kinds.Contains(IdentityDocumentKind.PassportPhoto)) Add("passportPhoto", "Members don't need a passport photo, so it isn't collected.");
        foreach (var d in documents)
        {
            var key = d.Kind switch { IdentityDocumentKind.IdFront => "idFront", IdentityDocumentKind.IdBack => "idBack", _ => "passportPhoto" };
            if (d.Content.Length == 0 || d.Content.Length > MaxFileBytes) Add(key, "Each file must be under 5 MB.");
            else if (Sniff(d.Content) is not { } actual || actual != d.ContentType ||
                     (d.Kind == IdentityDocumentKind.PassportPhoto && actual == "application/pdf"))
                Add(key, d.Kind == IdentityDocumentKind.PassportPhoto ? "Upload the photo as JPEG or PNG." : "Upload a JPEG, PNG or PDF file.");
        }
        DomainValidationException.ThrowIfAny(errors);

        return new IdentitySubmission
        {
            Id = Guid.CreateVersion7(now), UserId = user.Id, Level = level, IdType = idType,
            IdNumberProtected = protect(id), IdNumberLast4 = id[^4..],
            BvnProtected = bvnDigits is null ? null : protect(bvnDigits), BvnLast4 = bvnDigits?[^4..],
            PhoneNumber = user.PhoneNumber!, Status = IdentitySubmissionStatus.Pending, SubmittedAt = now,
        };
    }

    public void Approve(User reviewer, User subject, string? note, DateTimeOffset now)
    {
        Decide(reviewer, now);
        DecisionNote = string.IsNullOrWhiteSpace(note) ? null : Clip(note.Trim());
        Status = IdentitySubmissionStatus.Approved;
        subject.GrantIdentity(Level, now);
    }

    public void Reject(User reviewer, string? reason, DateTimeOffset now)
    {
        var r = (reason ?? "").Trim();
        if (r.Length is < 5 or > 500)
            throw new DomainValidationException(new Dictionary<string, string[]> { ["reason"] = ["Tell the user why, in 5–500 characters."] });
        Decide(reviewer, now);
        DecisionNote = r;
        Status = IdentitySubmissionStatus.Rejected;
    }

    /// <summary>Retention: drops the encrypted identifiers once the check is decided; last-4 digits and the outcome remain.</summary>
    public void PurgeSensitiveData(DateTimeOffset now)
    {
        if (Status == IdentitySubmissionStatus.Pending) throw new DomainConflictException("submission_pending", "Can't purge a check that is still being reviewed.");
        IdNumberProtected = null;
        BvnProtected = null;
        DataPurgedAt ??= now;
    }

    private void Decide(User reviewer, DateTimeOffset now)
    {
        if (Status != IdentitySubmissionStatus.Pending) throw new DomainConflictException("submission_decided", "This check has already been decided.");
        if (!reviewer.IsOperations) throw new DomainException("not_operations", "Only operations staff can review identity checks.");
        if (reviewer.Id == UserId) throw new DomainConflictException("own_submission", "You can't review your own identity check.");
        ReviewedBy = reviewer.Id;
        ReviewedAt = now;
    }

    private static string Digits(string? s, bool keepLetters) =>
        new((s ?? "").Where(c => keepLetters ? char.IsAsciiLetterOrDigit(c) : char.IsAsciiDigit(c)).Select(char.ToUpperInvariant).ToArray());

    private static string Clip(string s) => s.Length <= 500 ? s : s[..500];

    /// <summary>Content type from the file's own bytes — never trust the browser's label (brief §11.3, insecure upload).</summary>
    public static string? Sniff(byte[] b) =>
        b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF ? "image/jpeg"
        : b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47 && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A ? "image/png"
        : b.Length >= 5 && Encoding.ASCII.GetString(b, 0, 5) == "%PDF-" ? "application/pdf"
        : null;
}

/// <summary>An uploaded identity file, encrypted at rest. Content is removed when the retention period ends.</summary>
public sealed class IdentityDocument
{
    private IdentityDocument() { }
    public Guid Id { get; private set; }
    public Guid SubmissionId { get; private set; }
    public IdentityDocumentKind Kind { get; private set; }
    public string ContentType { get; private set; } = "";
    public long SizeBytes { get; private set; }
    public string Sha256 { get; private set; } = "";
    public byte[]? EncryptedContent { get; private set; }

    public static IdentityDocument Create(Guid submissionId, UploadedDocument d, Func<byte[], byte[]> protect, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(now), SubmissionId = submissionId, Kind = d.Kind, ContentType = d.ContentType, SizeBytes = d.Content.Length,
        Sha256 = Convert.ToHexStringLower(SHA256.HashData(d.Content)), EncryptedContent = protect(d.Content),
    };

    public void Purge() => EncryptedContent = null;
}

/// <summary>Nigerian phone numbers in E.164 (+234 followed by 10 digits).</summary>
public static class PhoneNumbers
{
    public static string Normalize(string? input)
    {
        var digits = new string((input ?? "").Where(char.IsAsciiDigit).ToArray());
        var national = digits.StartsWith("234") ? digits[3..] : digits.StartsWith('0') ? digits[1..] : digits;
        if (national.Length != 10 || national[0] is not ('7' or '8' or '9'))
            throw new DomainValidationException(new Dictionary<string, string[]> { ["phone"] = ["Enter a Nigerian mobile number, e.g. 0803 123 4567."] });
        return "+234" + national;
    }

    public static string Mask(string e164) => e164.Length > 7 ? e164[..7] + new string('•', e164.Length - 10) + e164[^3..] : e164;
}
