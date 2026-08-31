import { LogIn, LogOut, ShieldCheck, Upload } from 'lucide-react'
import { useIsAuthenticated, useMsal } from '@azure/msal-react'
import { EvidenceWorkspace } from './evidence/EvidenceWorkspace'
import { runtimeConfig } from './runtimeConfig'

function App() {
  const authenticated = useIsAuthenticated()
  const { instance, accounts } = useMsal()
  const account = accounts[0]

  async function signIn() {
    await instance.loginRedirect({ scopes: [runtimeConfig.apiScope] })
  }

  async function signOut() {
    await instance.logoutRedirect({ account })
  }

  return (
    <div className="app-shell">
      <aside className="sidebar">
        <div className="brand-mark">DL</div>
        <div className="brand-copy"><strong>DataLooM Studio</strong><span>Test workspace</span></div>
        <nav aria-label="Primary navigation">
          <button className="nav-item is-active" type="button"><Upload size={18} /><span>Evidence</span></button>
        </nav>
        <div className="security-state"><ShieldCheck size={18} /><span>Product authority enforced</span></div>
      </aside>

      <main className="main-surface">
        <header className="topbar">
          <div><p className="eyebrow">Non-production Test</p><h1>Evidence intake</h1></div>
          {authenticated ? (
            <div className="account-control">
              <span>{account?.name ?? account?.username}</span>
              <button className="icon-button" type="button" onClick={signOut} title="Sign out" aria-label="Sign out"><LogOut size={18} /></button>
            </div>
          ) : (
            <button className="command-button" type="button" onClick={signIn}><LogIn size={18} /> Sign in</button>
          )}
        </header>

        {authenticated ? <EvidenceWorkspace /> : (
          <section className="signed-out"><ShieldCheck size={32} /><h2>Authentication required</h2><p>Use the approved non-production Entra identity.</p></section>
        )}
      </main>
    </div>
  )
}

export default App
