# Database (#11)

PostgreSQL 17 schema of the dashboard: one schema per context (`ref`, `portfolio`, `financing`, `market`,
`bench`, `calc`, `reporting`, `meta`, `staging`, `audit`, `dpkeys`, `session`, `quartz`), monthly
partitions on `as_of_date`, row-level security per legal entity and least-privilege roles. The service
identities and their rights are defined in [docs/security/data-flow.md](../docs/security/data-flow.md#identities).

| Item | Where |
| --- | --- |
| Roles (cluster level, once per database) | [`roles.sql`](roles.sql) |
| Migrations (EF Core, reviewable SQL) | `src/CounterpartyRisk.Infrastructure.Persistence/Migrations/Sql/<migration>/NN_*.sql` |
| Idempotent script for review / DBA runs | [`migrations/idempotent.sql`](migrations/idempotent.sql) (generated, checked by a test) |
| Test fixture | `tests/CounterpartyRisk.IntegrationTests/Database/PostgresFixture.cs` |

## Order of operations

1. **Roles**: a DBA (or the platform bootstrap) runs `roles.sql` with a role holding `CREATEROLE`. It is
   idempotent and contains no password.
2. **Credentials**: each LOGIN role gets its password from the secret store (`ALTER ROLE ... PASSWORD`),
   never from the repository. Applications use `SSL Mode=VerifyFull` outside Development.
3. **Migrations**: the migration bundle runs with the `cr_migrator` connection only (it acts as `cr_owner`,
   which owns every object). The API and the workers never migrate.

```powershell
# Bundle (CI / release); the connection comes from the secret store at run time
dotnet ef migrations bundle --project src/CounterpartyRisk.Infrastructure.Persistence --self-contained -o efbundle.exe
./efbundle.exe --connection "<cr_migrator connection string>"

# After adding a migration: regenerate the reviewed script (a test fails when it is stale)
dotnet ef migrations script --idempotent --project src/CounterpartyRisk.Infrastructure.Persistence --output db/migrations/idempotent.sql
```

## Rules for new migrations

- Forward-only, static DDL in a numbered `.sql` file; no external input in DDL.
- A table carrying `legal_entity_id` calls `SELECT meta.apply_le_isolation('<schema>.<table>')` (in a `DO`
  block). `All_Tables_With_LegalEntityId_Should_Have_Forced_Rls` fails otherwise.
- A table partitioned by month is declared `PARTITION BY RANGE (as_of_date)`, with `as_of_date` in its
  primary key, and registered in `meta.partitioned_table`; `meta.ensure_partitions` then creates its
  partitions (and applies RLS to them).
- Enumerations are `varchar` + `CHECK` on the member names of the `Domain.Contracts` enums; add the
  constraint to `MigrationTests.EnumChecks`.
- Default privileges grant future tables to `cr_reader` / `cr_writer` per schema: a table that another
  role must own exclusively (limit tables of #27) revokes them explicitly.

## Reading with row-level security

The scope is set per transaction (`set_config('app.legal_entity_ids', ..., true)`) by
`LegalEntityScopeInterceptor`. A query outside a transaction has no scope and returns no row: read paths
use `Database.InLegalEntityScopeAsync(...)`; `SaveChanges` always opens a transaction.
