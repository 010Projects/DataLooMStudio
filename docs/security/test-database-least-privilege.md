# Test Database Least-Privilege Contract

Artifact: DLS-ENG-NONPROD-TEST-ENVIRONMENT-PREPARATION-001

Test and Production-intent environments use Microsoft Entra authentication and password authentication is disabled on PostgreSQL Flexible Server. API, worker, and migration each receive a distinct user-assigned managed identity and a username-only TLS connection string. Npgsql obtains short-lived Azure PostgreSQL access tokens; no runtime database password or administrator secret is injected.

| Principal | Login | Database authority |
| --- | --- | --- |
| Migration managed identity | Yes, Entra service principal and configured PostgreSQL Entra administrator | Explicit job invocation; role bootstrap, DDL/migrations, grants |
| API managed identity | Yes, Entra service principal | Connect and schema usage plus an explicit per-table `SELECT`/`INSERT`/`UPDATE` allowlist; no `DELETE`, blanket schema DML, default table privilege, or sequence authority |
| Worker managed identity | Yes, Entra service principal | Connect, `foundation` usage, execute four outbox functions; all direct foundation table grants revoked |
| `dls_outbox_executor` | No | Owns only the fixed outbox functions, has outbox select/update and `BYPASSRLS` so the dispatcher can claim all Tenant scopes |

The worker never receives `BYPASSRLS`. The security-definer functions return the stored Tenant/Workspace scope, require an unguessable lease ID for completion/failure, reclaim expired leases, cap claim batches, and expose no arbitrary SQL or scope input. `PUBLIC` execute is revoked after every owner transition.

Evidence versions, content verifications, Audit entries, Lineage relationships, and deletion-eligibility evaluations are append-only at the database boundary. A fixed-`search_path` trigger rejects direct `UPDATE` and `DELETE`, including operations attempted through a compromised API path. The API role receives no update authority for those tables. Stateful aggregates, including candidate decisions that transition to `Superseded`, retain only the narrow update rights required by their implemented transitions.

Product Actor, membership, permission-assignment, authority-elevation, and DisposalRecord tables remain stateful so revocation and approved lifecycle processing can operate. A second fixed-`search_path` database trigger denies row deletion and changes to their immutable identity, grant/request scope, attribution, policy, idempotency, and request-hash evidence. Audit preserves every allowed state transition.

The migration job runs once on explicit operator invocation with zero automatic retries. It creates Entra principals by object ID, applies EF migrations, reapplies grants idempotently, and must succeed before API/worker/web deployment. API startup never runs migrations. A failed migration or role bootstrap stops rollout.

Azure activation must validate the Entra administrator mapping, principal object IDs, exact API table grants, absence of default privileges and `DELETE`, direct-grant absence for the worker, function ownership, `rolcanlogin = false` and `rolbypassrls = true` only for `dls_outbox_executor`, immutable-table rejection, and hostile RLS tests before Test acceptance.
