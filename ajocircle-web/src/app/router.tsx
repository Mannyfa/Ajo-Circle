import { createBrowserRouter } from 'react-router-dom'
import { AppLayout, PublicLayout } from '../components/Layout'
import { LoginPage, RegisterPage, VerifyPage } from '../features/auth/AuthPages'
import { CreateGroupPage } from '../features/groups/CreateGroupPage'
import { DashboardPage } from '../features/groups/DashboardPage'
import { GroupLayout } from '../features/groups/GroupLayout'
import { GroupActivityTab, GroupContributionsTab, GroupMembersTab, GroupOverviewTab, GroupPayoutsTab } from '../features/groups/GroupTabs'
import { JoinPage } from '../features/members/JoinPage'
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
      {
        path: 'groups/:groupId',
        element: <GroupLayout />,
        children: [
          { index: true, element: <GroupOverviewTab /> },
          { path: 'members', element: <GroupMembersTab /> },
          { path: 'payouts', element: <GroupPayoutsTab /> },
          { path: 'contributions', element: <GroupContributionsTab /> },
          { path: 'activity', element: <GroupActivityTab /> },
        ],
      },
    ],
  },
])
