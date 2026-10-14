import { NavLink, Outlet } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'

const links = [
  { to: '/', label: 'Dashboard', end: true },
  { to: '/posts', label: 'Posts' },
  { to: '/comments', label: 'Comments' },
  { to: '/tags', label: 'Topics' },
  { to: '/subscribers', label: 'Subscribers' },
  { to: '/site', label: 'Site' },
  { to: '/portfolio', label: 'Portfolio' },
  { to: '/profile', label: 'Profile' },
  { to: '/activity', label: 'Activity' },
]

export function Layout() {
  const { session, logout } = useAuth()

  return (
    <div className="shell">
      <aside className="sidebar">
        <div className="sidebar__brand">Articles <span>Admin</span></div>

        <nav className="sidebar__nav" aria-label="Sections">
          {links.map((link) => (
            <NavLink key={link.to} to={link.to} end={link.end} className={({ isActive }) => `navlink${isActive ? ' navlink--active' : ''}`}>
              {link.label}
            </NavLink>
          ))}
        </nav>

        <div className="sidebar__foot">
          <div className="sidebar__who" title={session?.email}>{session?.email}</div>
          <button className="btn btn--small btn--on-dark" onClick={logout}>Sign out</button>
        </div>
      </aside>

      <main className="content">
        <Outlet />
      </main>
    </div>
  )
}
