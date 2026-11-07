# AjoCircle

Mobile-first web app for independent Nigerian ajo organizers to run rotating savings groups with clear,
shared records. Built from *AjoCircle — AI Product & Engineering Brief v1.0* (the source of truth).

> **Tracking only.** This release does not hold, collect, pool or transfer money. Scheduled obligations,
> organizer-recorded payments, member confirmations and provider-verified payments are distinct states.
> Nothing is described as escrow, protected or guaranteed.

## Stack

| Layer | Tech |
|---|---|
| Frontend | React 19 + TypeScript + Tailwind v4 + TanStack Query + React Router (`ajocircle-web/`) |
| API | ASP.NET Core (.NET 10) modular monolith (`src/`) |
| Database | PostgreSQL 17 + EF Core migrations (snake_case schema) |
| Tests | xUnit — domain unit tests; API tests on a disposable Postgres via Testcontainers |

```
src/AjoCircle.Domain          Entities, money, scheme rules, schedule + activation logic (pure, unit-tested)
src/AjoCircle.Application     Use cases, DTOs, authorization/ownership checks
src/AjoCircle.Infrastructure  EF Core DbContext, migrations, clock, password hashing, code sender
src/AjoCircle.Api             Controllers, cookie auth, CSRF header, rate limits, ProblemDetails
tests/AjoCircle.UnitTests     Domain tests (no infrastructure needed)
tests/AjoCircle.ApiTests      End-to-end API tests against real PostgreSQL (needs Docker)
ajocircle-web                 React app
```

## Run locally

Prerequisites: .NET 10 SDK, Node 24, Docker Desktop.

```bash
docker compose up -d                       # PostgreSQL on :5432 + Mailpit test inbox on :8025 (synthetic data only)
dotnet run --project src/AjoCircle.Api     # API on http://localhost:5092, applies migrations in Development
cd ajocircle-web && npm install && npm run dev   # UI on http://localhost:5173 (proxies /api)
```

In Development, verification emails go to **Mailpit**, a local test inbox: open http://localhost:8025 to read them.
Mailpit catches every message, so nothing is delivered to real addresses. The code is also written to the API console
(`[DEV ONLY] Verification code…`), so sign-up still works if Mailpit isn't running.

No email/SMS vendor has been chosen yet (brief §16.3). Any SMTP provider can be plugged in through `Email:Smtp`
(`Host`, `Port`, `EnableSsl`, `Username`, `Password`, `From`, `ProductName`); keep the password in user secrets or
environment variables. Outside Development, with no SMTP configured, the API refuses to send codes.

## Test

```bash
dotnet test tests/AjoCircle.UnitTests      # no dependencies
dotnet test tests/AjoCircle.ApiTests       # requires Docker running
cd ajocircle-web && npm run build && npm run lint
```

## Slice 1 — rotating groups

- Register → verify email (6-digit code, hashed, 5 attempts, 15 min) → sign in. HttpOnly SameSite=Strict session cookie,
  revocable via security stamp; PBKDF2 password hashing; login doesn't reveal whether an account exists.
- Organizer creates a **rotating ajo** draft → server-side preview of totals and schedule → **publish** freezes terms into
  immutable scheme version 1.
- Invitation links: 256-bit random token, only SHA-256 stored, expiry/max-uses/revoke. Public view shows group terms only — no member data.
- Member joins by accepting the **exact current terms version** (stale version → 409). Capacity enforced under a row lock;
  enrolment closes automatically when full.
- Payout order: join order (automatic) or organizer-assigned (validated permutation) before start.
- **Start** is a domain command: checks full membership, accepted versions, unique positions, start date — then creates
  rounds, N×N contribution obligations (`Due`) and N scheduled payouts in one transaction.
- Organizer isolation: anything outside your scope returns 404. Members see only their own obligations and no other members' emails.
- Append-only audit log for every sensitive action; DB triggers block UPDATE/DELETE on audit logs and scheme versions,
  and DELETE on obligations, rounds, payouts and memberships.
- UI: landing, trust, auth, dashboard (search/filter), 3-step create wizard, group workspace (overview, members & invites,
  payout schedule, contributions, activity), public join page. Status badges always pair text + glyph; green is reserved for provider-verified.

## Slice 2 — recorded payments, payouts and disputes

- Organizer **records a contribution paid outside the app** (date, method, optional reference, note) → `Recorded by organizer`.
  The member sees it, then **confirms** (`Confirmed by member`) or **reports a problem**. Neither is shown as verified.
- Organizer **records a payout outcome** (amount up to the scheduled pool) → the recipient confirms or disputes.
  A payout date arriving never changes its status; only a record does.
- **Corrections** never edit: the original is marked `Reversed` with who, when and why, and stays visible; corrected values
  become a new record linked back to it; a `record_amendments` row keeps old and new values.
- **Disputes** get a reference (e.g. `DSP-7K3M9QX2`) and a reply thread visible only to the member who raised it and the organizer.
- Organizer round totals (recorded vs expected, confirmed, disputed). Members don't see who else has paid.
- DB triggers: records and disputes may only change status columns, are frozen once reversed/resolved, and can't be
  deleted; amendments and dispute messages are fully append-only. Partial unique indexes allow one current record per
  obligation/payout and one open dispute per record, so double-submits get 409.
- Rate limit on raising disputes and replying (10/min, `RateLimits:DisputesPerMinute`).

## Slice 3 — notifications and account recovery

- **In-app notifications** (bell with unread count, list, mark read) for the brief §14.1 events that exist so far:
  member joined / group full (organizer), group started or cancelled (members), payment or payout recorded or
  corrected (that member), dispute raised / replied / withdrawn (the other party), contribution due soon or no payment
  recorded after the due date, and payout approaching (recipient).
- **Email copies via a durable outbox.** Each notification is written in the same transaction as the change that caused
  it; a background worker emails pending ones (`FOR UPDATE SKIP LOCKED`, retries with 1/2/4/8-minute backoff, then
  gives up). Email subjects and bodies carry only a short title and a link — never amounts, references or dispute text
  (brief §11.2). Users can turn notification emails off in **Settings**; the in-app copy always remains.
- **Reminders** run about once a minute and are idempotent (a dedupe key per user and event). An overdue reminder says
  "no payment is recorded yet", never that the member is late, because only the organizer's records are known.
- **Password recovery**: `/forgot-password` always answers the same way (no hint whether an account exists), max 3
  links per account per hour plus the IP rate limit. The link carries a 256-bit token (only its hash is stored), works
  once, expires in 30 minutes, and is removed from the address bar on open. Resetting signs out every session, burns
  any other outstanding links, marks the email verified and sends a "password changed" notice.
- Settings: `App:ProductName`, `App:PublicBaseUrl` (links in emails), `Notifications:WorkerEnabled`,
  `Notifications:PollSeconds`, `Notifications:ReminderSeconds`.

## Decisions made so far (review these)

1. **A rotating group starts only with exactly the member count in its accepted terms.** Pool = N×C and total commitment
   = C×R were what members accepted; starting short would change both. A short group can be cancelled; amendments come later.
2. **Payout order** is part of the accepted terms: "join order" or "organizer assigns before start".
3. **Product bounds** (configurable): 2–100 members, ₦100–₦10,000,000 per contribution, whole naira, payout offset 0–6 days,
   start date within 12 months and not in the past. Monthly dates clamp to month-end (31 Jan → 28/29 Feb → 31 Mar).
4. **Dates** are calendar dates in Africa/Lagos; timestamps are stored in UTC.
5. **Fees**: none — shown explicitly as "None" in the terms.
6. Organizer can join their own group as a member through an invite.
7. **A contribution record must equal the amount owed.** Part payments and overpayments are not modelled yet (open question below).
8. **A payout record may be less than the scheduled pool** but not more — a short payout is a real outcome worth recording.
9. **Only the organizer records**, and **only the member a record is about** can confirm or dispute it. An organizer who is
   also a member can't confirm or dispute their own entry, so it stays "Recorded by organizer".
10. **The organizer can't close a dispute by overruling the member.** It ends when the organizer corrects/reverses the
    record (resolved in the member's favour) or the member withdraws it (which confirms the record). Operations review
    for deadlocked disputes is a later slice.
11. Payment dates can't be in the future or more than a year back. Records are only accepted while the group is Active.
12. Reminders: "due soon" from 2 days before the due date; "no payment recorded" for up to 7 days after; "payout
    approaching" from 2 days before. The organizer doesn't get overdue alerts yet (the brief says "as configured").
13. Notification emails are on by default and can be turned off; security emails can't be.

## Slice 4 — fixed-term savings

- **Two variants** (brief §4.3): *individual savings* (each member saves towards their own target and gets back what
  they paid) and *shared savings* (one goal; the organizer picks the sharing rule — equal, in proportion to what each
  paid, or percentages the organizer assigns before the start). The rule is in the terms members accept, so it exists
  before anyone joins (§7.3).
- Terms: contribution, frequency, number of contributions (1–104, term ≤ 3 years), member limit (2–100), first due date,
  release offset (0–14 days), optional goal. Unlike a rotating ajo, a savings group **can start once 2 members have
  joined**, because each member's commitment doesn't depend on how many others joined.
- **Money owed back** is tracked as payouts of a new kind — *release*, *refund for leaving early*, *cancellation
  refund* — so the existing record / confirm / dispute / correct flow works for them. The organizer records paying it
  back (also after the group has ended), the member confirms or disputes. At most one of each kind per member (unique
  index), so a retried command can't owe twice.
- **Complete the term** (organizer, from the last contribution date): the recorded total is shared out by the agreed
  rule, exact to the kobo (largest-remainder split; amounts always add up). Contributions never recorded stay unpaid.
- **Leaving early** (the member, or recorded by the organizer): unpaid future contributions become "No longer owed",
  and what was recorded as paid becomes a refund. The member keeps read access to their history and refund.
- **Cancelling a running savings group** drops unpaid contributions and owes each member what they paid. (A running
  rotating ajo still can't be cancelled — no rule for unwinding payouts already made has been approved.)
- Settling money is blocked while a relevant dispute is open, because the amounts depend on it.
- Progress card (brief §7.3): target, scheduled, recorded, not recorded yet, owed back and paid back — kept separate,
  none presented as verified money. Members see their own figures; the organizer sees the group's.

## Slice 5 — identity verification and the operations workspace

- **Identity gate** (decision 7): creating or running a group needs *organizer* verification (phone code, ID document,
  BVN, passport photo); joining needs *member* verification (phone code, one ID document). Unverified users can sign in
  and look around; confirming, disputing and leaving a group stay open because they protect members.
- **Submissions** (`/app/verification`): phone verified by SMS code first; then a multipart upload. File types are read
  from the bytes (JPEG/PNG/PDF, photo JPEG/PNG only, ≤ 5 MB), never from the browser's label. ID number, BVN and files
  are encrypted with ASP.NET Core Data Protection; only the last 4 digits are stored in the clear. BVN and photos are
  not collected for member checks. Documents and full numbers are purged `Identity:DocumentRetentionDays` (default 30)
  after the decision; the outcome remains.
- **Operations staff** are granted from the command line, never via the web:
  `dotnet run --project src/AjoCircle.Api -- grant-operations someone@example.com` (and `revoke-operations`).
  They must set up two-step sign-in (authenticator-app codes, RFC 6238) and re-enter a code every 12 hours; the server
  checks role + second factor on every operations request, and the workspace returns 404 to everyone else.
- **Operations workspace** (`/operations`): identity check queue (view documents and full numbers on demand —
  every view audited — approve or reject with a reason; staff can't review themselves); escalated disputes
  (read everything, reply on the thread, decide *record stands* or *needs correcting* — decision 5; staff can't handle
  a group they belong to); audit log search by group, action, person and date.
- **Escalation** (decision 6): either party can escalate an open dispute 7 days after it was raised
  (`Disputes:EscalationAfterDays`). "Record stands" closes the dispute and the record can't be disputed again;
  "needs correcting" leaves it with the organizer, whose correction closes it.
- Development: SMS codes go to the Mailpit inbox as emails to `sms-<number>@sms.test`.
- **Production note:** the Data Protection key ring encrypts identity data and signs session cookies. It must be
  persisted to shared, backed-up storage (e.g. the database or a key vault) before real data is stored — if the keys
  are lost, encrypted documents can't be read. In development it lives in your user profile.

## Product decisions from the owner (7 Nov 2026)

1. **Shared-target savings: the organizer (group admin) decides how the money is shared.** To meet brief §7.3 ("store
   an explicit allocation formula before members join"), the organizer picks the rule when creating the group and it is
   locked into the terms members accept: *equal split*, *in proportion to what each member paid*, or *organizer assigns
   a percentage to each member before the group starts*.
2. **Early exit:** the money a member paid is returned to them. **Cancellation:** each member gets back what they paid.
   The money is held by the organizer, not the app, so the app shows what is owed back and the organizer records the
   refund; the member confirms or disputes it.
3. **Part payments are not accepted** — a contribution is recorded only when paid in full (already enforced).
4. **Disputes that the organizer and member can't settle are investigated by the operations team**, who resolve them.
   This needs the operations workspace (a later slice).

5. **Operations investigators decide the outcome only** (decision of 7 Nov 2026). They can uphold a disputed record
   or find that it needs correcting; the organizer makes any correction, so no record is edited invisibly.
6. **A dispute can be escalated to operations only after the organizer has had time to respond** — 7 days from when
   it was raised (configurable).
7. **Identity verification before anyone can act.** Organizers: government ID document, BVN, phone number and passport
   photo, reviewed by the operations team. Members: phone number and one ID document. Unverified users can sign in and
   look around but can't create, run or join groups.

## Still open (brief §16.3 — not chosen in code)

- **Lawful basis, retention and a verification provider for identity data.** Brief §11.2 says not to hold BVN or ID
  documents without a documented need *and* lawful basis, and to prefer a provider's reference over raw documents.
  Decision 7 is the need; the lawful basis (Nigeria Data Protection Act 2023 — consent wording, data-protection impact
  assessment, retention period) and a licensed BVN/ID verification provider still need legal review. Until then the
  app keeps the minimum: data encrypted at rest, documents purged after review, every operations view audited, and
  only synthetic data in development.


Payment partner and fund flow · organizer/member identity verification · post-start amendment approval threshold ·
default rules (what happens when a member stops paying) · fees · email/SMS vendor · hosting ·
when a rotating round or group counts as **completed**.

## Next slices (brief §17.1)

1. Reports and exports (contributions, payouts/owed back, outstanding) — CSV.
2. Persist the Data Protection key ring; choose a licensed BVN/ID verification provider and SMS vendor (open items).
3. Organizer reminder settings (overdue alerts, timing); reminders for money owed back.
4. Idempotency keys on record-creating requests; the automated 10-member end-to-end journey (brief §13.4).
5. Rotating round/group completion rules once the open question above is decided.
