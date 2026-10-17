import { useState } from 'react'
import type { FormEvent } from 'react'
import { Link, Outlet, useNavigate } from 'react-router-dom'
import { PORTFOLIO_URL } from '../lib/site'
import { useSite } from '../lib/siteContext'
import { GameDialog } from './GameDialog'
import { SubscribeForm } from './SubscribeForm'
import { ThemePicker } from './ThemePicker'

export function Layout() {
  const navigate = useNavigate()
  const { subscribeEnabled } = useSite()
  const [term, setTerm] = useState('')
  const [playing, setPlaying] = useState(false)

  function onSearch(event: FormEvent) {
    event.preventDefault()
    const trimmed = term.trim()
    if (trimmed) navigate(`/search?q=${encodeURIComponent(trimmed)}`)
  }

  return (
    <div className="app">
      <header className="header">
        <div className="header__inner">
          <Link to="/" className="brand">Articles<span className="brand__sep" aria-hidden="true">|</span><span className="brand__ur" lang="ur" dir="rtl">لکھت از احمد</span></Link>

          <form className="header__search" onSubmit={onSearch} role="search">
            <input
              type="search"
              placeholder="Search articles"
              aria-label="Search articles"
              value={term}
              onChange={(e) => setTerm(e.target.value)}
            />
          </form>

          <div className="header__nav">
            <button type="button" className="btn btn--ghost header__play" onClick={() => setPlaying(true)} aria-haspopup="dialog">
              <span aria-hidden="true">▶</span> Play
            </button>
            {PORTFOLIO_URL && (
              <nav aria-label="Elsewhere">
                <a className="btn btn--ghost" href={PORTFOLIO_URL}>About the author</a>
              </nav>
            )}
            <ThemePicker />
          </div>
        </div>
      </header>

      <Outlet />
      <GameDialog open={playing} onClose={() => setPlaying(false)} />

      <footer className="footer">
        {subscribeEnabled && (
          <div className="footer__subscribe">
            <SubscribeForm />
          </div>
        )}
        <div className="footer__inner">
          <span>Articles by Ahmad Nadeem</span>
          <nav aria-label="Site">
            {/* A separate page outside the reading app, so a plain link that loads it fresh. */}
            <a href="/play/" onClick={(event) => { event.preventDefault(); setPlaying(true) }}>Bored? Play a game</a>
            <Link to="/privacy">Privacy</Link>
            {PORTFOLIO_URL && <a href={PORTFOLIO_URL}>About the author</a>}
          </nav>
        </div>
      </footer>
    </div>
  )
}
