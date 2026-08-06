export type ModuleManifest = {
  name: string
  version: string
  boundaryKind: string
  requiresTenantContext: boolean
  requiresWorkspaceContext: boolean
  ownsTransactionalOutbox: boolean
  containsAiExecution: boolean
  responsibilities: string[]
  dependsOn: string[]
}

export async function loadModuleManifests(signal?: AbortSignal): Promise<ModuleManifest[]> {
  const response = await fetch('/api/modules', {
    headers: { Accept: 'application/json' },
    signal,
  })

  if (!response.ok) {
    throw new Error(`Module manifest request failed: ${response.status}`)
  }

  return response.json() as Promise<ModuleManifest[]>
}
