# Test Database Least-Privilege Contract

Artifact: DLS-ENG-NONPROD-TEST-ENVIRONMENT-PREPARATION-001

Test and Production-intent environments use Microsoft Entra authentication and password authentication is disabled on PostgreSQL Flexible Server. API, worker, and migration each receive a distinct user-assigned managed identity and a username-only TLS connection string. Npgsql obtains short-lived Azure PostgreSQL access tokens; no runtime database password or administrator secret is injected.

| Principal | Login | Database authority |
| --- | --- | --- |
| Migration managed identity | Yes, Entra service principal and configured PostgreSQL Entra administrator | Explicit job invocation; role bootstrap, DDL/migrations, grants |
| API managed identity | Yes, Entra service principal | Connect, schema usage, table DML, sequence use; RLS and immutable-table controls remain enforced |
| Worker managed identity | Yes, Entra service principal | Connect, `foundation` usage, execute four outbox functions; all direct foundation table grants revoked |
| `dls_outbox_executor` | No | Owns only the fixed outbox functions, has outbox select/update and `BYPASSRLS` so the dispatcher can claim all Tenant scopes |

The worker never receives `BYPASSRLS`. The security-definer functions return the stored Tenant/Workspace scope, require an unguessable lease ID for completion/failure, reclaim expired leases, cap claim batches, and expose no arbitrary SQL or scope input. `PUBLIC` execute is revoked after every owner transition.

The migration job runs once on explicit operator invocation with zero automatic retries. It creates Entra principals by object ID, applies EF migrations, reapplies grants idempotently, and must succeed before API/worker/web deployment. API startup never runs migrations. A failed migration or role bootstrap stops rollout.

Azure activation must validate the Entra administrator mapping, principal object IDs, direct-grant absence for the worker, function ownership, `rolcanlogin = false` and `rolbypassrls = true` only for `dls_outbox_executor`, and hostile RLS tests before Test acceptance.
