import type { IPublicClientApplication } from '@azure/msal-browser'
import { runtimeConfig } from '../runtimeConfig'

export type EvidenceRegistration = { evidenceId: string; versionId: string; lifecycleState: string; integrityState: string }
export type UploadAllocation = { storageObjectReference: string; uploadAuthority: string }
export type EvidenceReceipt = { lifecycleState: string; integrityOutcome: string; scanOutcome: string }
export type EvidenceSummary = EvidenceRegistration & {
  evidenceType: string
  classification: string
  verificationStatus: string
  originalFileName: string
  mediaType: string
  contentLength: number
  sha256Hash: string
  capturedAt: string
  lineageId: string
}

export async function authorizedJson<T>(instance: IPublicClientApplication, workspaceId: string, path: string, init?: RequestInit): Promise<T> {
  const account = instance.getActiveAccount() ?? instance.getAllAccounts()[0]
  if (!account) throw new Error('Authentication session is unavailable.')
  const token = await instance.acquireTokenSilent({ account, scopes: [runtimeConfig.apiScope] })
  const response = await fetch(path, {
    ...init,
    headers: {
      Accept: 'application/json',
      'Content-Type': 'application/json',
      Authorization: `Bearer ${token.accessToken}`,
      'X-Workspace-Id': workspaceId,
      ...init?.headers,
    },
  })
  if (!response.ok) {
    const problem = (await response.json().catch(() => null)) as { title?: string; detail?: string } | null
    throw new Error(problem?.detail ?? problem?.title ?? `Request failed with status ${response.status}.`)
  }
  return response.json() as Promise<T>
}

export async function sha256(file: File): Promise<string> {
  const hash = await crypto.subtle.digest('SHA-256', await file.arrayBuffer())
  return [...new Uint8Array(hash)].map((value) => value.toString(16).padStart(2, '0')).join('')
}
