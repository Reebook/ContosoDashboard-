# Quickstart / Validation Guide

Prerequisites: .NET 10 SDK, SQL Server LocalDB (or SQL Server), Azurite (`npm i -g azurite` or Docker) for local blob emulation in dev/test, an Entra ID app registration for the non-test environment. Details: [data-model.md](data-model.md), [contracts/](contracts/).

## 1. Configure (user-secrets / appsettings)
- `ConnectionStrings:DefaultConnection`
- `AzureAd:*` (Instance, TenantId, ClientId, CallbackPath)
- `Documents:Storage:AccountUri` (or `UseDevelopmentStorage=true` for Azurite), containers `quarantine`, `documents`
- `Documents:Scanner:Provider` = `Defender` | `Fake` (tests)
- `Documents:AuditRetentionDays` (per records policy; unset = keep)

## 2. Build, migrate, run
```powershell
cd ContosoDashboard
dotnet build
dotnet ef database update
azurite --silent --location $env:TEMP\azurite   # separate terminal
dotnet run
```

## 3. Automated validation
```powershell
dotnet test ContosoDashboard.Tests
```
Expect: authorization matrix (all 4 roles + unauthorized), upload validation (size/type/signature), quarantine/retry state machine, replacement-keeps-old-file, orphan-free failure path, revocation immediacy, audit rows survive delete.

## 4. Manual scenarios (map to spec stories)
1. US1: as Employee upload 3 files (clean PDF, 26 MB file, `.exe`) → per-file progress; oversized/unsupported rejected with reason; clean file appears only after scan = Clean.
2. US1/REQ-006: with `Scanner:Provider=Fake` set to Unavailable → file stays Pending and inaccessible to everyone; flip to Clean → becomes available (auto retry).
3. US2: My Documents sort/filter; search by title/tag/uploader/project returns only permitted docs; counts don't leak.
4. US3: edit metadata; replace with infected file → old file still downloadable; share to user and to a Department → recipients notified; revoke → access ends immediately; delete with confirmation.
5. US4: task page attach/upload inherits project; dashboard shows 5 recent + count; project members notified on Clean.
6. US5: as Administrator view audit/reports; as Employee `GET /api/admin/document-reports` → 403.
7. Security: request `/api/documents/{id}/download` for another user's doc → 404 identical to nonexistent id; blob URL anonymous → 403/404.

## 4b. Performance checks
Seed 500 docs/user: list < 2 s, search < 2 s, 25 MB upload < 30 s, preview < 3 s; run load test at the dashboard's peak concurrency (value to be supplied, see research R2).
