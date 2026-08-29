export type RuntimeConfig = {
  entraAuthority: string
  spaClientId: string
  apiScope: string
}

declare global {
  interface Window { __DLS_CONFIG__?: RuntimeConfig }
}

const configured = window.__DLS_CONFIG__
if (!configured?.entraAuthority || !configured.spaClientId || !configured.apiScope) {
  throw new Error('Public identity configuration is incomplete.')
}

export const runtimeConfig = Object.freeze(configured)
