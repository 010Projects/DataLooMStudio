import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'

const read = (path) => readFile(new URL(path, import.meta.url), 'utf8')

test('browser authentication uses MSAL public-client tokens without a client secret', async () => {
  const main = await read('../src/main.tsx')
  const api = await read('../src/api/evidence.ts')
  assert.match(main, /PublicClientApplication/)
  assert.match(api, /acquireTokenSilent/)
  assert.doesNotMatch(`${main}${api}`.toLowerCase(), /clientsecret|client_secret/)
})

test('Evidence workflow carries the canonical workspace header and malware-gated receipt', async () => {
  const api = await read('../src/api/evidence.ts')
  const workflow = await read('../src/evidence/EvidenceWorkspace.tsx')
  assert.match(api, /X-Workspace-Id/)
  assert.match(workflow, /content-received/)
  assert.match(workflow, /receipt\.lifecycleState !== 'Available'/)
  assert.match(workflow, /receipt\.scanOutcome !== 'Clean'/)
  assert.match(workflow, /requestReview/)
  assert.doesNotMatch(workflow, /skip.*scan|always.*clean/i)
})

test('web runtime remains non-root and injects only public identity configuration', async () => {
  const dockerfile = await read('../Dockerfile')
  const template = await read('../runtime-config/config.js.template')
  const nginx = await read('../nginx/default.conf.template')
  assert.match(dockerfile, /USER 101/)
  assert.match(template, /DLS_SPA_CLIENT_ID/)
  assert.match(nginx, /proxy_set_header Host \$proxy_host/)
  assert.doesNotMatch(template.toLowerCase(), /secret|password/)
})
