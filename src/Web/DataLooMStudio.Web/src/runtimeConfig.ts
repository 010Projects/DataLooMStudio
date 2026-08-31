export type RuntimeConfig = {
  entraAuthority: string
  entraTenantId: string
  spaClientId: string
  apiScope: string
}

declare global {
  interface Window { __DLS_CONFIG__?: RuntimeConfig }
}

const configured = window.__DLS_CONFIG__
if (!configured?.entraAuthority || !configured.entraTenantId || !configured.spaClientId || !configured.apiScope) {
  throw new Error('Public identity configuration is incomplete.')
}

const tenantIdPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i
const expectedAuthority = `https://login.microsoftonline.com/${configured.entraTenantId}/v2.0`
if (!tenantIdPattern.test(configured.entraTenantId) || configured.entraAuthority !== expectedAuthority) {
  throw new Error('Public identity authority must match the configured tenant-specific Microsoft Entra issuer.')
}

export const runtimeConfig = Object.freeze(configured)
