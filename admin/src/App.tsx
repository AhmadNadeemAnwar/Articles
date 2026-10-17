import { Suspense, lazy } from 'react'
import type { ReactElement } from 'react'
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom'
import { AuthProvider, useAuth } from './auth/AuthContext'
import { FeedbackProvider } from './components/feedback'
import { Layout } from './components/Layout'
import { Spinner } from './components/ui'
import { ActivityPage } from './pages/ActivityPage'
import { CommentsPage } from './pages/CommentsPage'
import { DashboardPage } from './pages/DashboardPage'
import { FeedbackPage } from './pages/FeedbackPage'
import { LoginPage } from './pages/LoginPage'
import { PortfolioEditPage } from './pages/PortfolioEditPage'
import { PortfolioPage } from './pages/PortfolioPage'
import { PostsPage } from './pages/PostsPage'
import { ProfilePage } from './pages/ProfilePage'
import { SitePage } from './pages/SitePage'
import { SubscribersPage } from './pages/SubscribersPage'
import { TagsPage } from './pages/TagsPage'

// The editor is by far the largest part of the app, so it loads only when a post is opened.
const PostEditPage = lazy(() => import('./pages/PostEditPage').then((m) => ({ default: m.PostEditPage })))
const editor = <Suspense fallback={<Spinner label="Opening the editor…" />}><PostEditPage /></Suspense>

/** Everything except the sign-in page needs a verified admin session. */
function RequireSession({ children }: { children: ReactElement }) {
  const { session, checking } = useAuth()

  if (checking) return <Spinner label="Checking your session…" />
  if (!session) return <Navigate to="/login" replace />
  return children
}

function LoginRoute() {
  const { session, checking } = useAuth()

  if (checking) return <Spinner label="Checking your session…" />
  if (session) return <Navigate to="/" replace />
  return <LoginPage />
}

export default function App() {
  return (
    <BrowserRouter>
      <AuthProvider>
        <FeedbackProvider>
          <Routes>
            <Route path="/login" element={<LoginRoute />} />

            <Route element={<RequireSession><Layout /></RequireSession>}>
              <Route path="/" element={<DashboardPage />} />
              <Route path="/posts" element={<PostsPage />} />
              <Route path="/posts/new" element={editor} />
              <Route path="/posts/:id/edit" element={editor} />
              <Route path="/comments" element={<CommentsPage />} />
              <Route path="/feedback" element={<FeedbackPage />} />
              <Route path="/tags" element={<TagsPage />} />
              <Route path="/subscribers" element={<SubscribersPage />} />
              <Route path="/site" element={<SitePage />} />
              <Route path="/profile" element={<ProfilePage />} />
              <Route path="/activity" element={<ActivityPage />} />
              <Route path="/portfolio" element={<Navigate to="/portfolio/projects" replace />} />
              <Route path="/portfolio/:collection" element={<PortfolioPage />} />
              {/* "_new" can never be a real file name (underscores are not allowed in them), so it cannot collide with an entry. */}
              <Route path="/portfolio/:collection/_new" element={<PortfolioEditPage />} />
              <Route path="/portfolio/:collection/:slug" element={<PortfolioEditPage />} />
              <Route path="*" element={<Navigate to="/" replace />} />
            </Route>
          </Routes>
        </FeedbackProvider>
      </AuthProvider>
    </BrowserRouter>
  )
}
