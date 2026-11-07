import { createBrowserRouter } from 'react-router-dom'
import { AppLayout, PublicLayout } from '../components/Layout'
import { ForgotPasswordPage, LoginPage, RegisterPage, ResetPasswordPage, VerifyPage } from '../features/auth/AuthPages'
import { NotificationsPage, SettingsPage } from '../features/notifications/NotificationPages'
import { DashboardPage } from '../features/groups/DashboardPage'
import { GroupLayout } from '../features/groups/GroupLayout'
import { GroupActivityTab, GroupContributionsTab, GroupMembersTab, GroupOverviewTab, GroupPayoutsTab } from '../features/groups/GroupTabs'
import { JoinPage } from '../features/members/JoinPage'
import { DisputeDetailPage, DisputesPage } from '../features/disputes/DisputePages'
import { ContributionDetailPage, PayoutDetailPage } from '../features/records/RecordPages'
import { LandingPage, PolicyPlaceholder, TrustPage } from '../pages/PublicPages'

// Less-used screens load on demand so the first visit stays light on mobile data (operations staff, reports,
// the create wizard and identity verification are each their own chunk).
const ops = () => import('../features/operations/OperationsPages')

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
      { path: 'groups/new', lazy: () => import('../features/groups/CreateGroupPage').then((m) => ({ Component: m.CreateGroupPage })) },
      { path: 'disputes', element: <DisputesPage /> },
      { path: 'notifications', element: <NotificationsPage /> },
      { path: 'settings', element: <SettingsPage /> },
      { path: 'verification', lazy: () => import('../features/identity/VerificationPage').then((m) => ({ Component: m.VerificationPage })) },
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
          { path: 'reports', lazy: () => import('../features/reports/ReportsTab').then((m) => ({ Component: m.GroupReportsTab })) },
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
        lazy: () => ops().then((m) => ({ Component: m.OperationsLayout })),
        children: [
          { index: true, lazy: () => ops().then((m) => ({ Component: m.VerificationQueuePage })) },
          { path: 'verifications/:id', lazy: () => ops().then((m) => ({ Component: m.VerificationReviewPage })) },
          { path: 'disputes', lazy: () => ops().then((m) => ({ Component: m.OperationsDisputesPage })) },
          { path: 'disputes/:disputeId', element: <DisputeDetailPage operations /> },
          { path: 'audit', lazy: () => ops().then((m) => ({ Component: m.AuditSearchPage })) },
        ],
      },
    ],
  },
])
