# DLS-PROD-AUTH-001
# Canonical Product Authority Role Taxonomy

## Decision

Status: `CONFIRMED`

The Product Office confirmed the canonical Product role taxonomy and Product Authority principles for role-dependent Engineering capability expansion.

## Canonical Role Names

1. Tenant Owner
2. Workspace Owner
3. Evidence Contributor
4. Evidence Reader
5. Reviewer
6. Decision Approver
7. Governance Administrator
8. Retention Administrator
9. Legal Hold Administrator
10. Commercial Administrator
11. Billing Administrator
12. Support Operator
13. Security Operator
14. Repository Administrator
15. Platform Administrator
16. Auditor

## Authority Principle

External authentication is correlated into IdentityAccess, Product Actor, membership, roles/grants/policy attributes, effective Product permission, effective entitlement, bounded-context policy, assignment/scope/SoD, authority freshness, then permit or deny.

Roles are governed permission bundles. They are not endpoint-level authority checks and they are not the primary runtime authority contract.

Product permissions remain the stable runtime authority contract.

## Confirmed Invariants

- Review authority requires permission, assignment, scope, SoD, and fresh authority.
- Decision approval requires explicit Decision authority and cannot be inherited from administrative privilege.
- Effective Entitlements determine commercial capability availability only; they do not grant human Product authority.
- Tenant Owner and Workspace Owner do not receive implicit Evidence, Review, or Decision authority.
- Commercial, Billing, Support, Security, Repository, and Platform authority confer no implicit Product content or approval authority.
- Separation of duties is policy-driven and extensible.
- Stale or contradictory authority denies sensitive action.
- Assurance by Design is a canonical cross-cutting Product Architecture principle.

## Continuing Boundaries

- Production Authority: not granted.
- Restricted Pilot authority: not granted.
- Production Evidence: not authorized.
- Production deployment: not authorized.
- AI execution: not authorized.
