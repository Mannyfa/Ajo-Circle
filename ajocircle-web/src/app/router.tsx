import { createBrowserRouter, Navigate } from 'react-router-dom'
import { AppLayout } from '../components/Layout'
import { PublicLayout } from '../components/PublicLayout'
import { RouteError } from './RouteError'
import { ForgotPasswordPage, LoginPage, RegisterPage, ResetPasswordPage, VerifyPage } from '../features/auth/AuthPages'
import { NotificationsPage, SettingsPage } from '../features/notifications/NotificationPages'
import { DashboardPage, GroupsPage } from '../features/groups/DashboardPage'
import { GroupLayout } from '../features/groups/GroupLayout'
import { GroupActivityTab, GroupContributionsTab, GroupMembersTab, GroupOverviewTab, GroupPayoutsTab } from '../features/groups/GroupTabs'
import { JoinPage } from '../features/members/JoinPage'
import { DisputeDetailPage, DisputesPage } from '../features/disputes/DisputePages'
import { ContributionDetailPage, PayoutDetailPage } from '../features/records/RecordPages'
import { HelpPage } from '../features/help/HelpPage'
import {
  AboutPage, ContactPage, FeaturesPage, ForbiddenPage, HomePage, HowItWorksPage, LegalPage, NotFoundPage, OrganizersPage, PricingPage,
} from '../pages/PublicPages'

// Less-used screens load on demand so the first visit stays light on mobile data (operations staff, reports,
// the create wizard and identity verification are each their own chunk).
const ops = () => import('../features/operations/OperationsPages')

// URL map follows brief §5.5.
// Route map follows the Product & UI Engineering Specification §17. Every route has the global error boundary.
export const router = createBrowserRouter([
  {
    element: <PublicLayout />,
    errorElement: <RouteError />,
    children: [
      { path: '/', element: <HomePage /> },
      { path: '/how-it-works', element: <HowItWorksPage /> },
      { path: '/features', element: <FeaturesPage /> },
      { path: '/organizers', element: <OrganizersPage /> },
      { path: '/pricing', element: <PricingPage /> },
      { path: '/about', element: <AboutPage /> },
      { path: '/contact', element: <ContactPage /> },
      { path: '/privacy', element: <LegalPage kind="privacy" /> },
      { path: '/terms', element: <LegalPage kind="terms" /> },
      { path: '/financial-services-disclaimer', element: <LegalPage kind="disclaimer" /> },
      { path: '/trust', element: <Navigate to="/financial-services-disclaimer" replace /> },
      { path: '/register', element: <RegisterPage /> },
      { path: '/login', element: <LoginPage /> },
      { path: '/verify-account', element: <VerifyPage /> },
      { path: '/verify', element: <Navigate to="/verify-account" replace /> },
      { path: '/forgot-password', element: <ForgotPasswordPage /> },
      { path: '/reset-password', element: <ResetPasswordPage /> },
      { path: '/join/:token', element: <JoinPage /> },
      { path: '/403', element: <ForbiddenPage /> },
      { path: '/404', element: <NotFoundPage /> },
      { path: '*', element: <NotFoundPage /> },
    ],
  },
  {
    path: '/app',
    element: <AppLayout />,
    errorElement: <RouteError />,
    children: [
      { index: true, element: <Navigate to="/app/dashboard" replace /> },
      { path: 'dashboard', element: <DashboardPage /> },
      { path: 'groups', element: <GroupsPage /> },
      { path: 'help', element: <HelpPage /> },
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
    errorElement: <RouteError />,
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
