import {
  Activity,
  Archive,
  BotOff,
  Boxes,
  CheckCircle2,
  ChevronDown,
  CircleGauge,
  Database,
  Fingerprint,
  GitBranch,
  History,
  KeyRound,
  LockKeyhole,
  Menu,
  RadioTower,
  RefreshCw,
  Scale,
  Search,
  ShieldCheck,
  SlidersHorizontal,
  Workflow,
} from 'lucide-react'
import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { loadModuleManifests, type ModuleManifest } from './api/foundation'

const fallbackModules: ModuleManifest[] = [
  {
    name: 'Tenancy',
    version: '1.0.0',
    boundaryKind: 'Core',
    requiresTenantContext: false,
    requiresWorkspaceContext: false,
    ownsTransactionalOutbox: true,
    containsAiExecution: false,
    responsibilities: ['Authoritative tenant records', 'External identity authority mapping'],
    dependsOn: [],
  },
  {
    name: 'Workspaces',
    version: '1.0.0',
    boundaryKind: 'Core',
    requiresTenantContext: true,
    requiresWorkspaceContext: false,
    ownsTransactionalOutbox: true,
    containsAiExecution: false,
    responsibilities: ['Workspace catalog', 'Tenant-owned workspace membership boundary'],
    dependsOn: ['Tenancy'],
  },
  {
    name: 'Evidence',
    version: '1.0.0',
    boundaryKind: 'EvidenceConsistency',
    requiresTenantContext: true,
    requiresWorkspaceContext: true,
    ownsTransactionalOutbox: true,
    containsAiExecution: false,
    responsibilities: ['Evidence metadata', 'Evidence integrity proof', 'ADR-014 consistency boundary'],
    dependsOn: ['Tenancy', 'Workspaces', 'Lineage', 'Retention', 'Audit'],
  },
  {
    name: 'Lineage',
    version: '1.0.0',
    boundaryKind: 'Lineage',
    requiresTenantContext: true,
    requiresWorkspaceContext: true,
    ownsTransactionalOutbox: true,
    containsAiExecution: false,
    responsibilities: ['Immutable lineage identifiers', 'Versioned relationships'],
    dependsOn: ['Tenancy', 'Workspaces'],
  },
  {
    name: 'Audit',
    version: '1.0.0',
    boundaryKind: 'Audit',
    requiresTenantContext: true,
    requiresWorkspaceContext: true,
    ownsTransactionalOutbox: true,
    containsAiExecution: false,
    responsibilities: ['Append-only audit entries', 'Actor and correlation traceability'],
    dependsOn: ['Tenancy', 'Workspaces'],
  },
  {
    name: 'Retention',
    version: '1.0.0',
    boundaryKind: 'Retention',
    requiresTenantContext: true,
    requiresWorkspaceContext: true,
    ownsTransactionalOutbox: true,
    containsAiExecution: false,
    responsibilities: ['Retention policies', 'Legal holds', 'Deletion eligibility decisions'],
    dependsOn: ['Tenancy', 'Workspaces', 'Evidence'],
  },
  {
    name: 'Commercial',
    version: '1.0.0',
    boundaryKind: 'Commercial',
    requiresTenantContext: true,
    requiresWorkspaceContext: true,
    ownsTransactionalOutbox: true,
    containsAiExecution: false,
    responsibilities: ['Commercial capability entitlement', 'Plan and feature boundaries'],
    dependsOn: ['Tenancy', 'Workspaces'],
  },
  {
    name: 'Lifecycle',
    version: '1.0.0',
    boundaryKind: 'Lifecycle',
    requiresTenantContext: true,
    requiresWorkspaceContext: true,
    ownsTransactionalOutbox: true,
    containsAiExecution: false,
    responsibilities: ['State definitions', 'State transition auditability'],
    dependsOn: ['Tenancy', 'Workspaces', 'Audit'],
  },
  {
    name: 'Workflows',
    version: '1.0.0',
    boundaryKind: 'Workflow',
    requiresTenantContext: true,
    requiresWorkspaceContext: true,
    ownsTransactionalOutbox: true,
    containsAiExecution: false,
    responsibilities: ['Workflow definitions', 'Workflow run tracking', 'No lifecycle ownership'],
    dependsOn: ['Tenancy', 'Workspaces', 'Lifecycle', 'Audit'],
  },
  {
    name: 'AiGovernance',
    version: '1.0.0',
    boundaryKind: 'AiGovernanceBoundary',
    requiresTenantContext: true,
    requiresWorkspaceContext: true,
    ownsTransactionalOutbox: true,
    containsAiExecution: false,
    responsibilities: ['AI policy boundary', 'Model execution prohibition', 'Prompt and result governance metadata only'],
    dependsOn: ['Tenancy', 'Workspaces', 'Audit', 'Commercial'],
  },
]

const tabs = ['Overview', 'Evidence', 'Lineage', 'Retention', 'AI Boundary'] as const

type Tab = (typeof tabs)[number]

function App() {
  const [modules, setModules] = useState<ModuleManifest[]>(fallbackModules)
  const [activeTab, setActiveTab] = useState<Tab>('Overview')
  const [workspaceOnly, setWorkspaceOnly] = useState(true)
  const [query, setQuery] = useState('')
  const [apiState, setApiState] = useState<'live' | 'fallback'>('fallback')

  useEffect(() => {
    const controller = new AbortController()

    loadModuleManifests(controller.signal)
      .then((loaded) => {
        setModules(loaded)
        setApiState('live')
      })
      .catch(() => {
        setApiState('fallback')
      })

    return () => controller.abort()
  }, [])

  const filteredModules = useMemo(() => {
    const normalizedQuery = query.trim().toLowerCase()

    return modules.filter((module) => {
      const matchesScope = !workspaceOnly || module.requiresWorkspaceContext
      const matchesQuery =
        normalizedQuery.length === 0 ||
        module.name.toLowerCase().includes(normalizedQuery) ||
        module.boundaryKind.toLowerCase().includes(normalizedQuery)

      return matchesScope && matchesQuery
    })
  }, [modules, query, workspaceOnly])

  const workspaceScoped = modules.filter((module) => module.requiresWorkspaceContext).length
  const outboxOwned = modules.filter((module) => module.ownsTransactionalOutbox).length
  const aiExecutionModules = modules.filter((module) => module.containsAiExecution).length

  return (
    <div className="shell">
      <aside className="sidebar" aria-label="Workspace navigation">
        <div className="brand">
          <div className="brand-mark">DL</div>
          <div>
            <strong>DataLooM Studio</strong>
            <span>Engineering Foundation</span>
          </div>
        </div>

        <nav className="nav-list">
          <button className="nav-item is-active" type="button">
            <CircleGauge size={18} />
            Foundation
          </button>
          <button className="nav-item" type="button">
            <Fingerprint size={18} />
            Evidence
          </button>
          <button className="nav-item" type="button">
            <GitBranch size={18} />
            Lineage
          </button>
          <button className="nav-item" type="button">
            <Archive size={18} />
            Retention
          </button>
          <button className="nav-item" type="button">
            <BotOff size={18} />
            AI Boundary
          </button>
        </nav>

        <div className="sidebar-footer">
          <KeyRound size={18} />
          <span>Entra scoped access</span>
        </div>
      </aside>

      <main className="workspace">
        <header className="topbar">
          <button className="icon-button" type="button" aria-label="Open navigation">
            <Menu size={20} />
          </button>
          <div className="search-box">
            <Search size={18} />
            <input
              aria-label="Filter modules"
              value={query}
              onChange={(event) => setQuery(event.target.value)}
              placeholder="Filter module or boundary"
            />
          </div>
          <button className="icon-button" type="button" aria-label="Refresh status">
            <RefreshCw size={19} />
          </button>
          <button className="profile-button" type="button">
            <span>Restricted Pilot</span>
            <ChevronDown size={16} />
          </button>
        </header>

        <section className="summary-band">
          <div>
            <p className="eyebrow">DLS-ENG-FOUNDATION-001</p>
            <h1>Greenfield Solution Foundation</h1>
          </div>
          <div className={`api-pill ${apiState}`}>
            <RadioTower size={17} />
            {apiState === 'live' ? 'API live' : 'Local manifest'}
          </div>
        </section>

        <section className="metric-grid" aria-label="Foundation metrics">
          <Metric icon={<Boxes size={20} />} label="Modules" value={modules.length.toString()} accent="teal" />
          <Metric icon={<LockKeyhole size={20} />} label="Workspace scoped" value={workspaceScoped.toString()} accent="indigo" />
          <Metric icon={<Database size={20} />} label="Outbox owners" value={outboxOwned.toString()} accent="green" />
          <Metric icon={<BotOff size={20} />} label="AI executors" value={aiExecutionModules.toString()} accent="rose" />
        </section>

        <section className="control-row">
          <div className="tabs" role="tablist" aria-label="Foundation views">
            {tabs.map((tab) => (
              <button
                key={tab}
                className={activeTab === tab ? 'is-active' : ''}
                onClick={() => setActiveTab(tab)}
                role="tab"
                type="button"
              >
                {tab}
              </button>
            ))}
          </div>
          <label className="toggle-control">
            <input
              checked={workspaceOnly}
              onChange={(event) => setWorkspaceOnly(event.target.checked)}
              type="checkbox"
            />
            <span>Workspace scoped</span>
          </label>
        </section>

        <section className="work-surface">
          <div className="module-grid" aria-label={`${activeTab} modules`}>
            {filteredModules.map((module) => (
              <ModuleTile key={module.name} module={module} />
            ))}
          </div>

          <div className="side-panel">
            <div className="panel-heading">
              <SlidersHorizontal size={18} />
              <h2>Boundary Controls</h2>
            </div>
            <BoundaryRow icon={<ShieldCheck size={18} />} label="Tenant isolation" state="Required" />
            <BoundaryRow icon={<Fingerprint size={18} />} label="Evidence integrity" state="ADR-014" />
            <BoundaryRow icon={<History size={18} />} label="Versioned lineage" state="Enabled" />
            <BoundaryRow icon={<Workflow size={18} />} label="Lifecycle split" state="Separated" />
            <BoundaryRow icon={<Scale size={18} />} label="Legal hold" state="Enforced" />
            <BoundaryRow icon={<BotOff size={18} />} label="AI execution" state="Outside Engineering" />
          </div>
        </section>
      </main>
    </div>
  )
}

function Metric({
  icon,
  label,
  value,
  accent,
}: {
  icon: ReactNode
  label: string
  value: string
  accent: 'teal' | 'indigo' | 'green' | 'rose'
}) {
  return (
    <div className={`metric ${accent}`}>
      <span>{icon}</span>
      <div>
        <strong>{value}</strong>
        <p>{label}</p>
      </div>
    </div>
  )
}

function ModuleTile({ module }: { module: ModuleManifest }) {
  return (
    <article className="module-tile">
      <div className="module-title">
        <span className="status-dot" />
        <div>
          <h2>{module.name}</h2>
          <p>{module.boundaryKind}</p>
        </div>
      </div>
      <div className="module-flags">
        <span>{module.requiresWorkspaceContext ? 'Workspace' : 'Platform'}</span>
        <span>{module.ownsTransactionalOutbox ? 'Outbox' : 'No outbox'}</span>
        <span>{module.containsAiExecution ? 'AI execution' : 'No AI execution'}</span>
      </div>
      <p className="module-detail">{module.responsibilities[0]}</p>
      <div className="dependency-row">
        <Activity size={16} />
        <span>{module.dependsOn.length === 0 ? 'No inbound dependency' : module.dependsOn.join(', ')}</span>
      </div>
    </article>
  )
}

function BoundaryRow({ icon, label, state }: { icon: ReactNode; label: string; state: string }) {
  return (
    <div className="boundary-row">
      <span className="boundary-icon">{icon}</span>
      <span>{label}</span>
      <strong>
        <CheckCircle2 size={16} />
        {state}
      </strong>
    </div>
  )
}

export default App
