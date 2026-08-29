# Non-Production Identity Contract

Artifact: DLS-ENG-NONPROD-TEST-ENVIRONMENT-PREPARATION-001

## Registration Boundary

- API registration: single-tenant non-production application exposing delegated scope `Dls.Access`; application ID URI and audience are deployment parameters.
- SPA registration: separate public client using authorization code with PKCE; no client credential, implicit flow, or application permission.
- Redirect URIs: exact HTTPS Test web origins only. Localhost is permitted only in Development registration/configuration.
- API validation: issuer, signature, audience, token lifetime, and `scp=Dls.Access` are required in Test.

## Canonical Claims

| Claim | Use |
|---|---|
| `tid` | External tenant correlation input |
| `oid` | Canonical external actor subject |
| `scp` | Delegated API access gate only |
| `name` / `preferred_username` | Display only; never authority |

The browser supplies `X-Workspace-Id`. It is untrusted context, not authority. IdentityAccess correlates `tid`/`oid` to the Product Actor and evaluates membership, permission assignment, scope, separation of duties, entitlements where applicable, and authority freshness. Entra roles and owners receive no implicit Evidence, Review, or Decision authority.

Authentication failure returns `401`. Missing/malformed tenant or workspace context returns `400`. Authenticated but unauthorized Product actions return `403` without disclosing cross-scope resource existence. Stale, revoked, contradictory, or unavailable authority denies sensitive actions and records Product Authority audit evidence.

## Activation Prerequisites

Security must approve the Test tenant, API/SPA registrations, consent, redirect URIs, claim mapping, Conditional Access posture, and synthetic Test actors. Real IDs replace all `.invalid` and zero-GUID placeholders only under explicit Test deployment authority.
