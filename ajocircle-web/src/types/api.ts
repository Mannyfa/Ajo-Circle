// Mirrors AjoCircle.Application.Contracts. Money is always integer minor units (kobo) + currency.

export type Money = { amountMinor: number; currency: 'NGN' }
export type Frequency = 'Weekly' | 'Biweekly' | 'Monthly'
export type PayoutOrderMethod = 'JoinOrder' | 'OrganizerAssigned'
export type SchemeType = 'RotatingAjo' | 'FixedTermIndividual' | 'FixedTermShared'
export type GroupStatus = 'Draft' | 'OpenForJoining' | 'ReadyToStart' | 'Active' | 'Suspended' | 'Completed' | 'Cancelled'
export type ObligationStatus =
  | 'Due' | 'SubmittedForReview' | 'RecordedByOrganizer' | 'ConfirmedByMember'
  | 'VerifiedByProvider' | 'Disputed' | 'Reversed' | 'Cancelled'
export type PayoutStatus = 'Scheduled' | 'RecordedByOrganizer' | 'ConfirmedByRecipient' | 'Disputed'
export type ViewerRole = 'Organizer' | 'Member'

export type IdentityLevel = 'None' | 'Member' | 'Organizer'
export type Me = {
  id: string; email: string; displayName: string; contactVerified: boolean; createdAt: string
  identityLevel: IdentityLevel; phoneVerified: boolean
  isOperations: boolean; mfaEnabled: boolean; operationsSessionActive: boolean
}

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
/** payoutDate is null for fixed-term periods. */
export type RoundPlan = { roundNumber: number; contributionDueDate: string; payoutDate: string | null }
export type SchemePreview = { summary: SchemeSummary | null; schedule: RoundPlan[]; fixedTermSummary: FixedTermSummary | null }

// ---- Fixed-term savings (slice 4) ----
export type SchemeVariant = 'Individual' | 'Shared'
export type SavingsAllocation = 'OwnContributions' | 'Equal' | 'Proportional' | 'OrganizerAssigned'
export type FixedTermRules = {
  variant: SchemeVariant; contributionAmountMinor: number; currency: 'NGN'; frequency: Frequency; memberCount: number
  periods: number; startDate: string; releaseOffsetDays: number; allocation: SavingsAllocation; goal: string | null
}
export type FixedTermSummary = {
  memberLimit: number; periods: number; contributionPerPeriod: Money; targetPerMember: Money; maxScheduledTotal: Money
  firstDueDate: string; lastDueDate: string; releaseDate: string
}
export type SavingsProgress = {
  forYou: boolean; members: number; target: Money; scheduled: Money; recorded: Money; notYetRecorded: Money; owedBack: Money; owedBackRecorded: Money
}
export type PayoutKind = 'RotatingPayout' | 'Release' | 'ExitRefund' | 'CancellationRefund'
export type MembershipStatus = 'Active' | 'Removed' | 'Exited'
export type PayoutListItem = {
  id: string; kind: PayoutKind; roundNumber: number | null; recipientName: string; recipientMembershipId: string; isYours: boolean
  amount: Money; scheduledDate: string; status: PayoutStatus
}

export type GroupListItem = {
  id: string; name: string; schemeType: SchemeType; status: GroupStatus; viewerRole: ViewerRole
  memberCount: number; capacity: number; contribution: Money; frequency: Frequency; startDate: string
}

export type GroupDetail = {
  id: string; name: string; schemeType: SchemeType; status: GroupStatus; viewerRole: ViewerRole
  organizerName: string
  /** Rotating ajo only. */
  rules: RotatingRules | null; summary: SchemeSummary | null
  /** Fixed-term savings only. */
  fixedTermRules: FixedTermRules | null; fixedTermSummary: FixedTermSummary | null
  capacity: number
  currentSchemeVersion: number | null; currentSchemeVersionId: string | null; activeMemberCount: number
  createdAt: string; publishedAt: string | null; startedAt: string | null
  cancelledAt: string | null; cancellationReason: string | null; completedAt: string | null
  myMembership: {
    membershipId: string; payoutPosition: number | null; acceptedSchemeVersion: number; joinedAt: string
    status: MembershipStatus; exitedAt: string | null; shareBasisPoints: number | null
  } | null
  savings: SavingsProgress | null
  allowedActions: string[]
}

export type Member = {
  membershipId: string; displayName: string; email: string | null; joinSequence: number
  payoutPosition: number | null; acceptedSchemeVersion: number; joinedAt: string; isYou: boolean
  status: MembershipStatus; exitedAt: string | null; exitReason: string | null; shareBasisPoints: number | null
}

export type Round = {
  id: string; roundNumber: number; contributionDueDate: string; payoutDate: string | null; status: 'Scheduled' | 'Completed'
  /** Payout fields are null for fixed-term periods. */
  payoutRecipientName: string | null; scheduledPayout: Money | null; payoutStatus: PayoutStatus | null
  payoutId: string | null; payoutIsYours: boolean
  /** Organizer only. "Recorded" counts organizer records, which are not verified money. */
  progress: RoundProgress | null
}
export type RoundProgress = { expected: number; recorded: number; confirmed: number; disputed: number; expectedTotal: Money; recordedTotal: Money }

export type Obligation = {
  id: string; roundNumber: number; memberName: string; membershipId: string; amount: Money; dueDate: string; status: ObligationStatus
}

export type AuditEntry = { id: string; action: string; entityType: string; entityId: string; actorName: string | null; metadata: string | null; createdAt: string }

export type Paged<T> = { items: T[]; page: number; pageSize: number; total: number }

export type Invitation = { id: string; createdAt: string; expiresAt: string; maxUses: number | null; useCount: number; revokedAt: string | null; state: string }
export type CreatedInvitation = { id: string; token: string; expiresAt: string; maxUses: number | null }

export type PublicInvitation = {
  groupName: string; organizerName: string; schemeType: SchemeType; schemeVersionId: string; schemeVersion: number
  rules: RotatingRules | null; summary: SchemeSummary | null; spotsRemaining: number; canJoin: boolean; cannotJoinReason: string | null
  fixedTermRules: FixedTermRules | null; fixedTermSummary: FixedTermSummary | null
}
export type JoinResult = { groupId: string; membershipId: string }

// ---- Organizer-reported records (slice 2) ----
export type PaymentMethod = 'Cash' | 'BankTransfer' | 'PosOrMobileMoney' | 'Other'
export type RecordStatus = 'RecordedByOrganizer' | 'ConfirmedByMember' | 'Disputed' | 'Reversed'
export type RecordKind = 'ContributionRecord' | 'PayoutRecord'

export type PaymentInput = { amountMinor: number; paidOn: string; method: PaymentMethod; evidenceReference: string | null; note: string | null }

export type PaymentRecord = {
  id: string; amount: Money; paidOn: string; method: PaymentMethod; evidenceReference: string | null; note: string | null
  status: RecordStatus; recordedByName: string; recordedAt: string; confirmedByName: string | null; confirmedAt: string | null
  reversedByName: string | null; reversedAt: string | null; reversalReason: string | null; replacesRecordId: string | null
  openDisputeId: string | null; allowedActions: string[]
}

export type ObligationDetail = {
  id: string; groupId: string; roundNumber: number; memberName: string; membershipId: string; isYou: boolean; amount: Money
  dueDate: string; status: ObligationStatus; records: PaymentRecord[]; disputes: DisputeSummary[]; allowedActions: string[]
}

export type PayoutDetail = {
  id: string; groupId: string; kind: PayoutKind; roundNumber: number | null; recipientName: string; recipientMembershipId: string; isYou: boolean
  scheduledAmount: Money; scheduledDate: string; status: PayoutStatus; records: PaymentRecord[]; disputes: DisputeSummary[]; allowedActions: string[]
}

// ---- Disputes ----
export type DisputeCategory = 'DidNotHappen' | 'WrongAmount' | 'WrongDate' | 'Other'
export type DisputeStatus = 'Open' | 'Escalated' | 'Resolved'
export type DisputeResolution = 'WithdrawnByMember' | 'RecordCorrected' | 'RecordReversed' | 'UpheldByOperations'
export type OperationsDecision = 'RecordUpheld' | 'CorrectionRequired'

export type DisputeSummary = {
  id: string; reference: string; groupId: string; groupName: string; recordType: RecordKind; recordId: string; subjectId: string
  subjectLabel: string; category: DisputeCategory; status: DisputeStatus; resolution: DisputeResolution | null
  raisedByName: string; raisedByYou: boolean; raisedAt: string; resolvedAt: string | null
}
export type DisputeMessage = { id: string; authorName: string; isYou: boolean; isOrganizer: boolean; isOperations: boolean; body: string; createdAt: string }
export type DisputeDetail = {
  summary: DisputeSummary; description: string; resolutionNote: string | null; resolvedByName: string | null
  correctiveRecordId: string | null; record: PaymentRecord; messages: DisputeMessage[]; allowedActions: string[]
  escalation: {
    canEscalate: boolean; escalationAllowedFrom: string; escalatedAt: string | null; escalatedByName: string | null
    decision: OperationsDecision | null; decisionNote: string | null; decidedAt: string | null
  }
}

// ---- Identity verification & operations (slice 5) ----
export type IdDocumentType = 'NationalId' | 'InternationalPassport' | 'DriversLicence' | 'VotersCard'
export type SubmissionStatus = 'Pending' | 'Approved' | 'Rejected'
export type VerificationStatus = {
  level: IdentityLevel; phoneMasked: string | null; phoneVerified: boolean
  latest: { id: string; level: IdentityLevel; status: SubmissionStatus; submittedAt: string; reviewedAt: string | null; rejectionReason: string | null } | null
}
export type VerificationQueueItem = {
  id: string; userName: string; userEmail: string; level: IdentityLevel; idType: IdDocumentType; status: SubmissionStatus; submittedAt: string; reviewedAt: string | null
}
export type VerificationDetail = {
  summary: VerificationQueueItem; phoneMasked: string; idNumberLast4: string; bvnLast4: string | null; sensitiveDataPurged: boolean
  decisionNote: string | null; reviewedByName: string | null
  documents: { id: string; kind: 'IdFront' | 'IdBack' | 'PassportPhoto'; contentType: string; sizeBytes: number; available: boolean }[]
  allowedActions: string[]
}
export type AuditSearchItem = {
  id: string; action: string; entityType: string; entityId: string; groupId: string | null; groupName: string | null
  actorName: string | null; actorEmail: string | null; metadata: string | null; createdAt: string
}

// ---- Notifications & settings (slice 3) ----
export type NotificationItem = {
  id: string; type: string; groupId: string | null; title: string; body: string; linkPath: string; createdAt: string; readAt: string | null
}
export type NotificationPage = { items: NotificationItem[]; page: number; pageSize: number; total: number; unread: number }
export type Settings = { emailNotifications: boolean }
