import { useState } from 'react'
import type { FormEvent } from 'react'
import { useNavigate } from 'react-router-dom'
import { useAuth } from '../auth/AuthContext'
import { ErrorNote } from '../components/ui'

export function LoginPage() {
  const { login } = useAuth()
  const navigate = useNavigate()
  const [email, setEmail] = useState('')
  const [code, setCode] = useState('')
  const [error, setError] = useState<string | null>(null)
  const [busy, setBusy] = useState(false)

  async function submit(event: FormEvent) {
    event.preventDefault()
    setBusy(true)
    setError(null)
    try {
      await login(email, code.replace(/\s/g, ''))
      navigate('/', { replace: true })
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not sign you in.')
      // A code is single-use, so a failed attempt always needs a fresh one.
      setCode('')
    } finally {
      setBusy(false)
    }
  }

  return (
    <main className="login">
      <form className="login__card" onSubmit={submit} aria-busy={busy}>
        <h1>Articles Admin</h1>
        <p className="muted">Sign in with your email and the 6-digit code from your authenticator app.</p>

        {error && <ErrorNote message={error} />}

        <div className="field">
          <label htmlFor="email">Email</label>
          <input id="email" type="email" autoComplete="username" value={email} onChange={(e) => setEmail(e.target.value)} required autoFocus disabled={busy} />
        </div>

        <div className="field">
          <label htmlFor="code">Authenticator code</label>
          <input
            id="code"
            inputMode="numeric"
            autoComplete="one-time-code"
            pattern="[0-9 ]{6,7}"
            maxLength={7}
            placeholder="123456"
            value={code}
            onChange={(e) => setCode(e.target.value)}
            required
            // Locked while the code is being checked: a code is single-use, so editing it mid-check would only confuse.
            disabled={busy}
          />
        </div>

        <button className="btn btn--primary btn--block" disabled={busy}>
          {busy ? 'Signing in…' : 'Sign in'}
        </button>
      </form>
    </main>
  )
}
