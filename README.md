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
docker compose up -d                       # PostgreSQL on localhost:5432 (synthetic data only)
dotnet run --project src/AjoCircle.Api     # API on http://localhost:5092, applies migrations in Development
cd ajocircle-web && npm install && npm run dev   # UI on http://localhost:5173 (proxies /api)
```

In Development, email verification codes are **written to the API console** (`[DEV ONLY] Verification code…`).
No email/SMS vendor has been chosen yet (brief §16.3); outside Development the API refuses to send codes.

## Test

```bash
dotnet test tests/AjoCircle.UnitTests      # no dependencies
dotnet test tests/AjoCircle.ApiTests       # requires Docker running
cd ajocircle-web && npm run build && npm run lint
```

## What this first slice delivers

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

## Decisions made in this slice (review these)

1. **A rotating group starts only with exactly the member count in its accepted terms.** Pool = N×C and total commitment
   = C×R were what members accepted; starting short would change both. A short group can be cancelled; amendments come later.
2. **Payout order** is part of the accepted terms: "join order" or "organizer assigns before start".
3. **Product bounds** (configurable): 2–100 members, ₦100–₦10,000,000 per contribution, whole naira, payout offset 0–6 days,
   start date within 12 months and not in the past. Monthly dates clamp to month-end (31 Jan → 28/29 Feb → 31 Mar).
4. **Dates** are calendar dates in Africa/Lagos; timestamps are stored in UTC.
5. **Fees**: none — shown explicitly as "None" in the terms.
6. Organizer can join their own group as a member through an invite.

## Still open (brief §16.3 — not chosen in code)

Payment partner and fund flow · organizer/member identity verification · post-start amendment approval threshold ·
default/exit/cancellation/refund rules · fixed-term shared-target distribution formula · fees · email/SMS vendor · hosting.

## Next slices (brief §17.1)

1. Record externally reported contributions (`RecordedByOrganizer`) with evidence reference; member confirm/dispute; amendments/reversals linked to originals.
2. Record external payout outcomes (no transfers).
3. Disputes with references, responses and resolution trail.
4. Fixed-term savings (individual target and shared target variants).
5. Notifications via outbox + worker; reminders.
6. Reports/exports, password recovery, operations workspace, idempotency keys.
