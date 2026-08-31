import { AlertTriangle, CheckCircle2, FileUp, LoaderCircle } from 'lucide-react'
import { useMsal } from '@azure/msal-react'
import { useState, type FormEvent } from 'react'
import { authorizedJson, sha256, type EvidenceReceipt, type EvidenceRegistration, type EvidenceSummary, type UploadAllocation } from '../api/evidence'

type Stage = 'idle' | 'hashing' | 'registering' | 'uploading' | 'scanning' | 'complete' | 'failed'

export function EvidenceWorkspace() {
  const { instance } = useMsal()
  const [workspaceId, setWorkspaceId] = useState('')
  const [classification, setClassification] = useState('Internal')
  const [file, setFile] = useState<File>()
  const [stage, setStage] = useState<Stage>('idle')
  const [error, setError] = useState('')
  const [summary, setSummary] = useState<EvidenceSummary>()
  const [reviewState, setReviewState] = useState('Not requested')

  async function submit(event: FormEvent) {
    event.preventDefault()
    if (!file || !isUuid(workspaceId)) return
    setError('')
    setSummary(undefined)
    try {
      setStage('hashing')
      const contentHash = await sha256(file)
      const root = `/api/v1/workspaces/${workspaceId}/evidence`
      setStage('registering')
      const registration = await authorizedJson<EvidenceRegistration>(instance, workspaceId, root, {
        method: 'POST', headers: { 'Idempotency-Key': crypto.randomUUID() },
        body: JSON.stringify({
          evidenceType: 'Document', classification, originalFileName: file.name,
          mediaType: file.type || 'application/octet-stream', declaredSize: file.size, contentHash,
          storageObjectReference: `pending/${crypto.randomUUID()}`, retentionPolicyKey: 'default',
        }),
      })
      const allocation = await authorizedJson<UploadAllocation>(instance, workspaceId, `${root}/${registration.evidenceId}/upload-allocation`, {
        method: 'POST', headers: { 'Idempotency-Key': crypto.randomUUID() }, body: '{}',
      })
      setStage('uploading')
      const upload = await fetch(allocation.uploadAuthority, {
        method: 'PUT', headers: { 'x-ms-blob-type': 'BlockBlob', 'Content-Type': file.type || 'application/octet-stream' }, body: file,
      })
      if (!upload.ok) throw new Error(`Blob upload failed with status ${upload.status}.`)
      setStage('scanning')
      const receipt = await authorizedJson<EvidenceReceipt>(instance, workspaceId, `${root}/${registration.evidenceId}/versions/${registration.versionId}/content-received`, {
        method: 'POST', headers: { 'Idempotency-Key': crypto.randomUUID() },
        body: JSON.stringify({ storageObjectReference: allocation.storageObjectReference }),
      })
      if (receipt.lifecycleState !== 'Available' || receipt.scanOutcome !== 'Clean') {
        throw new Error('Evidence was not accepted by integrity and malware validation.')
      }
      const loaded = await authorizedJson<EvidenceSummary>(instance, workspaceId, `${root}/${registration.evidenceId}`)
      setSummary(loaded)
      setReviewState('Not requested')
      setStage('complete')
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Evidence intake failed.')
      setStage('failed')
    }
  }

  async function requestReview() {
    if (!summary) return
    try {
      const result = await authorizedJson<{ state: string }>(instance, workspaceId, `/api/v1/workspaces/${workspaceId}/evidence/${summary.evidenceId}/versions/${summary.versionId}/reviews`, {
        method: 'POST', headers: { 'Idempotency-Key': crypto.randomUUID() }, body: JSON.stringify({ reviewKind: 'Standard' }),
      })
      setReviewState(result.state)
    } catch (caught) {
      setError(caught instanceof Error ? caught.message : 'Review request failed.')
    }
  }

  const busy = !['idle', 'complete', 'failed'].includes(stage)
  return (
    <div className="evidence-layout">
      <section className="intake-panel">
        <div className="section-heading"><div><p className="eyebrow">Workspace command</p><h2>Register Evidence</h2></div><span className="boundary-badge">Fail closed</span></div>
        <form onSubmit={submit}>
          <label>Workspace ID<input value={workspaceId} onChange={(event) => setWorkspaceId(event.target.value)} required /></label>
          <label>Classification<select value={classification} onChange={(event) => setClassification(event.target.value)}><option>Internal</option><option>Confidential</option><option>Restricted</option></select></label>
          <label className="file-control"><FileUp size={20} /><span>{file?.name ?? 'Select Evidence file'}</span><input type="file" onChange={(event) => setFile(event.target.files?.[0])} required /></label>
          <button className="command-button" type="submit" disabled={busy || !file || !isUuid(workspaceId)}>{busy ? <LoaderCircle className="spin" size={18} /> : <FileUp size={18} />} Register and verify</button>
        </form>
        <ol className="stage-list" aria-label="Evidence intake stages">
          {['Metadata registered', 'Content uploaded', 'Malware scan completed', 'Audit and lineage persisted'].map((label, index) => (
            <li key={label} className={stageIndex(stage) > index ? 'done' : ''}><CheckCircle2 size={16} />{label}</li>
          ))}
        </ol>
        {error && <div className="error-state" role="alert"><AlertTriangle size={18} />{error}</div>}
      </section>
      <section className="record-panel">
        <div className="section-heading"><div><p className="eyebrow">Current record</p><h2>Evidence summary</h2></div></div>
        {summary ? <div className="record-details">
          <Detail label="Evidence ID" value={summary.evidenceId} mono /><Detail label="File" value={summary.originalFileName} />
          <Detail label="State" value={`${summary.lifecycleState} / ${summary.verificationStatus}`} /><Detail label="Classification" value={summary.classification} />
          <Detail label="Lineage ID" value={summary.lineageId} mono /><Detail label="SHA-256" value={summary.sha256Hash} mono />
          <div className="review-row"><span>Review</span><strong>{reviewState}</strong><button type="button" onClick={requestReview}>Request review</button></div>
        </div> : <div className="empty-state">No Evidence selected.</div>}
      </section>
    </div>
  )
}

function Detail({ label, value, mono = false }: { label: string; value: string; mono?: boolean }) {
  return <div className="detail-row"><span>{label}</span><strong className={mono ? 'mono' : ''}>{value}</strong></div>
}

function isUuid(value: string) { return /^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value) }
function stageIndex(stage: Stage) { return ({ idle: 0, hashing: 0, registering: 1, uploading: 1, scanning: 2, complete: 4, failed: 0 })[stage] }
