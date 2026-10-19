# Implementation Plan: Document Upload and Management

**Branch**: `001-document-upload-management` (git currently on `main`; no feature branch was created) | **Date**: 2026-10-18 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/001-document-upload-management/spec.md`

## Summary

Add secure document upload, browse/search, sharing, task/dashboard integration, and admin audit/reporting to the existing ASP.NET Core Blazor Server dashboard. Files live in a private Azure Blob container (quarantine → clean), are malware-scanned before exposure (unavailable/indeterminate ⇒ stay quarantined and retry), are served only through server endpoints that re-authorize every request, and every action is audited. Sign-in moves to Entra ID. See [research.md](research.md) for decisions.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`), Blazor Server + Razor Pages  
**Primary Dependencies**: EF Core 10 SqlServer, Microsoft.Identity.Web 2.15 (existing); add `Azure.Storage.Blobs`, `Azure.Identity`; Defender for Storage scanning (behind `IMalwareScanner`)  
**Storage**: SQL Server (metadata, shares, audit); Azure Blob Storage (content, SSE at rest)  
**Testing**: New `ContosoDashboard.Tests` — xUnit, EF Core SQLite/InMemory, WebApplicationFactory, Azurite, fake scanner; load test (NBomber/k6)  
**Target Platform**: Azure App Service with managed identity; TLS 1.3 minimum  
**Project Type**: Single web application (Blazor Server)  
**Performance Goals**: 25 MB upload ≤30 s; list of 500 ≤2 s; search ≤2 s; preview ≤3 s; inherit dashboard availability/peak-load  
**Constraints**: Cloud-only; no public blob URLs; 25 MB/file; allow-listed types; request-time authorization; no orphaned records  
**Scale/Scope**: 5,000 employees, 4 roles; ~4 new tables, ~6 services, ~4 pages/components, 4 endpoints

## Constitution Check

`.specify/memory/constitution.md` is still the **unfilled template** (placeholders only), so no ratified gates exist. **Issue**: run `/speckit.constitution` to define them. In the interim the plan is checked against spec-derived principles:

| Gate | Status |
|---|---|
| Security first (authz at request time, no direct URLs, scanning, audit) | Pass (D2, D3, D5, D8) |
| Fits existing architecture (scoped services, EF, Blazor) | Pass |
| Testability (abstractions for storage/scanner; tests added) | Pass |
| Simplicity (no search service, no versioning) | Pass |

Post-design re-check: Pass. Justified additions in Complexity Tracking.

## Project Structure

### Documentation (this feature)

```text
specs/001-document-upload-management/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── document-http-api.md
│   └── service-interfaces.md
└── tasks.md             # created later by /speckit.tasks
```

### Source Code (repository root)

```text
ContosoDashboard/
├── Models/              # + Document.cs, DocumentShare.cs, TaskDocument.cs, DocumentActivity.cs, enums
├── Data/                # ApplicationDbContext (+DbSets, config); Migrations/ (new)
├── Services/            # + DocumentService, DocumentShareService, DocumentAuthorizationService,
│                        #   DocumentAuditService, BlobDocumentStorage, DefenderMalwareScanner,
│                        #   DocumentScanWorker (BackgroundService); edit DashboardService, NotificationService
├── Endpoints/           # + DocumentEndpoints.cs (upload/replace/download/preview/reports)
├── Pages/               # + Documents.razor (My/Shared/Project tabs), upload component,
│                        #   AdminDocumentReports.razor; edit Index.razor, Tasks.razor, ProjectDetails.razor
├── Shared/              # + RecentDocumentsWidget.razor; edit NavMenu.razor
├── Program.cs           # Entra OIDC, DI, endpoints, hub limits, hosted worker
└── appsettings*.json
ContosoDashboard.Tests/  # new test project (+ solution file)
```

**Structure Decision**: Extend the existing single web project following its `Models/Services/Pages` conventions; add one test project.

## Complexity Tracking

| Addition | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| Storage + scanner abstractions | Swappable backend, deterministic tests (spec retained requirement) | Direct SDK calls untestable offline |
| Background scan worker | Required retry-until-resolved quarantine (REQ-006) | Inline scan blocks upload and can't retry |
| EF migrations | `EnsureCreated` won't add tables to existing DBs | Manual SQL is error-prone |
| Department as "team" | No Team entity exists | New Team model is out of this feature's scope (risk R1) |

## Known Issues / Risks
See research.md R1–R5 (Team concept, undefined peak-load numbers, retention period, Defender licensing, Entra migration). The spec header says branch `main`; the setup script resolved `001-document-upload-management` via `SPECIFY_FEATURE` and `.specify/feature.json`.
