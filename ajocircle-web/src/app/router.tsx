import { createBrowserRouter } from 'react-router-dom'
import { AppLayout, PublicLayout } from '../components/Layout'
import { ForgotPasswordPage, LoginPage, RegisterPage, ResetPasswordPage, VerifyPage } from '../features/auth/AuthPages'
import { NotificationsPage, SettingsPage } from '../features/notifications/NotificationPages'
import { VerificationPage } from '../features/identity/VerificationPage'
import { AuditSearchPage, OperationsDisputesPage, OperationsLayout, VerificationQueuePage, VerificationReviewPage } from '../features/operations/OperationsPages'
import { CreateGroupPage } from '../features/groups/CreateGroupPage'
import { DashboardPage } from '../features/groups/DashboardPage'
import { GroupLayout } from '../features/groups/GroupLayout'
import { GroupActivityTab, GroupContributionsTab, GroupMembersTab, GroupOverviewTab, GroupPayoutsTab } from '../features/groups/GroupTabs'
import { JoinPage } from '../features/members/JoinPage'
import { DisputeDetailPage, DisputesPage } from '../features/disputes/DisputePages'
import { ContributionDetailPage, PayoutDetailPage } from '../features/records/RecordPages'
import { LandingPage, PolicyPlaceholder, TrustPage } from '../pages/PublicPages'

// URL map follows brief §5.5.
export const router = createBrowserRouter([
  {
    element: <PublicLayout />,
    children: [
      { path: '/', element: <LandingPage /> },
      { path: '/trust', element: <TrustPage /> },
      { path: '/terms', element: <PolicyPlaceholder title="Terms of Service" /> },
      { path: '/privacy', element: <PolicyPlaceholder title="Privacy Notice" /> },
      { path: '/register', element: <RegisterPage /> },
      { path: '/login', element: <LoginPage /> },
      { path: '/verify', element: <VerifyPage /> },
      { path: '/forgot-password', element: <ForgotPasswordPage /> },
      { path: '/reset-password', element: <ResetPasswordPage /> },
      { path: '/join/:token', element: <JoinPage /> },
      { path: '*', element: <PolicyPlaceholder title="Page not found" /> },
    ],
  },
  {
    path: '/app',
    element: <AppLayout />,
    children: [
      { index: true, element: <DashboardPage /> },
      { path: 'groups', element: <DashboardPage /> },
      { path: 'groups/new', element: <CreateGroupPage /> },
      { path: 'disputes', element: <DisputesPage /> },
      { path: 'notifications', element: <NotificationsPage /> },
      { path: 'settings', element: <SettingsPage /> },
      { path: 'verification', element: <VerificationPage /> },
      { path: 'disputes/:disputeId', element: <DisputeDetailPage /> },
      {
        path: 'groups/:groupId',
        element: <GroupLayout />,
        children: [
          { index: true, element: <GroupOverviewTab /> },
          { path: 'members', element: <GroupMembersTab /> },
          { path: 'payouts', element: <GroupPayoutsTab /> },
          { path: 'payouts/:payoutId', element: <PayoutDetailPage /> },
          { path: 'contributions', element: <GroupContributionsTab /> },
          { path: 'contributions/:obligationId', element: <ContributionDetailPage /> },
          { path: 'activity', element: <GroupActivityTab /> },
        ],
      },
    ],
  },
  {
    // Internal operations workspace (brief §5.5 URL map). Role and second factor are enforced by the API.
    path: '/operations',
    element: <AppLayout />,
    children: [
      {
        element: <OperationsLayout />,
        children: [
          { index: true, element: <VerificationQueuePage /> },
          { path: 'verifications/:id', element: <VerificationReviewPage /> },
          { path: 'disputes', element: <OperationsDisputesPage /> },
          { path: 'disputes/:disputeId', element: <DisputeDetailPage operations /> },
          { path: 'audit', element: <AuditSearchPage /> },
        ],
      },
    ],
  },
])
