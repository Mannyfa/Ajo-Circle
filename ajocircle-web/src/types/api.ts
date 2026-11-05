// Mirrors AjoCircle.Application.Contracts. Money is always integer minor units (kobo) + currency.

export type Money = { amountMinor: number; currency: 'NGN' }
export type Frequency = 'Weekly' | 'Biweekly' | 'Monthly'
export type PayoutOrderMethod = 'JoinOrder' | 'OrganizerAssigned'
export type SchemeType = 'RotatingAjo' | 'FixedTermIndividual' | 'FixedTermShared'
export type GroupStatus = 'Draft' | 'OpenForJoining' | 'ReadyToStart' | 'Active' | 'Suspended' | 'Completed' | 'Cancelled'
export type ObligationStatus =
  | 'Due' | 'SubmittedForReview' | 'RecordedByOrganizer' | 'ConfirmedByMember'
  | 'VerifiedByProvider' | 'Disputed' | 'Reversed'
export type PayoutStatus = 'Scheduled'
export type ViewerRole = 'Organizer' | 'Member'

export type Me = { id: string; email: string; displayName: string; contactVerified: boolean; createdAt: string }

export type RotatingRules = {
  contributionAmountMinor: number
  currency: 'NGN'
  frequency: Frequency
  memberCount: number
  startDate: string // yyyy-mm-dd (calendar date, Lagos)
  payoutOffsetDays: number
  payoutOrder: PayoutOrderMethod
}

export type SchemeSummary = {
  members: number
  rounds: number
  contributionPerMemberPerRound: Money
  expectedPoolPerRound: Money
  totalCommitmentPerMember: Money
  totalScheduledContributions: Money
  firstDueDate: string
  finalPayoutDate: string
}
export type RoundPlan = { roundNumber: number; contributionDueDate: string; payoutDate: string }
export type SchemePreview = { summary: SchemeSummary; schedule: RoundPlan[] }

export type GroupListItem = {
  id: string; name: string; schemeType: SchemeType; status: GroupStatus; viewerRole: ViewerRole
  memberCount: number; capacity: number; contribution: Money; frequency: Frequency; startDate: string
}

export type GroupDetail = {
  id: string; name: string; schemeType: SchemeType; status: GroupStatus; viewerRole: ViewerRole
  organizerName: string; rules: RotatingRules; summary: SchemeSummary
  currentSchemeVersion: number | null; currentSchemeVersionId: string | null; activeMemberCount: number
  createdAt: string; publishedAt: string | null; startedAt: string | null
  cancelledAt: string | null; cancellationReason: string | null
  myMembership: { membershipId: string; payoutPosition: number | null; acceptedSchemeVersion: number; joinedAt: string } | null
  allowedActions: string[]
}

export type Member = {
  membershipId: string; displayName: string; email: string | null; joinSequence: number
  payoutPosition: number | null; acceptedSchemeVersion: number; joinedAt: string; isYou: boolean
}

export type Round = {
  id: string; roundNumber: number; contributionDueDate: string; payoutDate: string; status: 'Scheduled' | 'Completed'
  payoutRecipientName: string | null; scheduledPayout: Money; payoutStatus: PayoutStatus
}

export type Obligation = {
  id: string; roundNumber: number; memberName: string; membershipId: string; amount: Money; dueDate: string; status: ObligationStatus
}

export type AuditEntry = { id: string; action: string; entityType: string; entityId: string; actorName: string | null; metadata: string | null; createdAt: string }

export type Paged<T> = { items: T[]; page: number; pageSize: number; total: number }

export type Invitation = { id: string; createdAt: string; expiresAt: string; maxUses: number | null; useCount: number; revokedAt: string | null; state: string }
export type CreatedInvitation = { id: string; token: string; expiresAt: string; maxUses: number | null }

export type PublicInvitation = {
  groupName: string; organizerName: string; schemeType: SchemeType; schemeVersionId: string; schemeVersion: number
  rules: RotatingRules; summary: SchemeSummary; spotsRemaining: number; canJoin: boolean; cannotJoinReason: string | null
}
export type JoinResult = { groupId: string; membershipId: string }
