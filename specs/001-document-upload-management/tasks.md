# Tasks: Document Upload and Management

**Input**: Design documents from `/specs/001-document-upload-management/`  
**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/`, `quickstart.md`  
**Tech stack**: C# / .NET 10, ASP.NET Core Blazor Server + Razor Pages, EF Core 10 SQL Server, Azure Blob Storage, Entra ID / Microsoft.Identity.Web.  
**Tests**: Included because the plan defines a new xUnit test project and the quickstart requires automated validation. Keep tests specific to each independently testable story.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Create the test/build structure and feature configuration points required by the planned implementation.

- [ ] T001 Create `ContosoDashboard.Tests/ContosoDashboard.Tests.csproj` targeting `net10.0` and add `ContosoDashboard.sln` at the repository root with the web and test projects.
- [ ] T002 [P] Add xUnit, Microsoft.NET.Test.Sdk, EF Core SQLite/InMemory, ASP.NET Core.Mvc.Testing, and Azure Blob test dependencies to `ContosoDashboard.Tests/ContosoDashboard.Tests.csproj`.
- [ ] T003 [P] Add non-secret document storage, scanner, and audit-retention configuration keys with safe placeholders to `ContosoDashboard/appsettings.json` and `ContosoDashboard/appsettings.Development.json`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Establish identity, authorization, persistence evolution, and shared service seams before feature stories begin.

- [ ] T004 Define and implement the EF migration adoption strategy for the existing `EnsureCreated()` database in `ContosoDashboard/Data/ApplicationDbContext.cs` and `ContosoDashboard/Program.cs`, preserving existing installations while enabling future migrations.
- [ ] T005 Configure Entra ID OpenID Connect and cookie sign-in from `AzureAd` settings in `ContosoDashboard/Program.cs`; gate the existing mock login to Development only in `ContosoDashboard/Pages/Login.cshtml.cs`.
- [ ] T006 Add an actor context that requires UserId, name, email, role, and department claims and denies actions with a clear message if any claim is missing in `ContosoDashboard/Services/ActorContext.cs`.
- [ ] T007 Define the shared request-time authorization interface and access result types used by document reads and management in `ContosoDashboard/Services/IDocumentAuthorizationService.cs`.
- [ ] T008 [P] Define blob-storage, malware-scanner, and document-audit service contracts and their options in `ContosoDashboard/Services/IDocumentStorage.cs`, `ContosoDashboard/Services/IMalwareScanner.cs`, `ContosoDashboard/Services/IDocumentAuditService.cs`, and `ContosoDashboard/Services/DocumentOptions.cs`.

**Checkpoint**: Base project/test structure, Entra identity context, schema evolution approach, and shared contracts are ready; user stories may now proceed independently subject to their own files and dependencies.

---

## Phase 3: User Story 1 - Upload and secure work documents (Priority: P1) 🎯 MVP

**Goal**: Employees upload files with metadata and per-file status; invalid, pending, and rejected content is never exposed, and scanning retries until a definitive result.

**Independent Test**: Using an employee account, upload a supported clean file and a file that violates a limit or fails scanning; verify validation, progress, metadata, quarantine/retry outcomes, and that content stays inaccessible until clean without relying on search or dashboard integration.

### Tests for User Story 1

- [ ] T009 [P] [US1] Add upload validation, idempotency, blob/DB compensation, per-file result, and pending/rejected-access tests in `ContosoDashboard.Tests/Services/DocumentUploadTests.cs`.
- [ ] T010 [P] [US1] Add upload endpoint status-code, multipart metadata, request-size, and audit tests in `ContosoDashboard.Tests/Endpoints/DocumentUploadEndpointTests.cs`.
- [ ] T011 [P] [US1] Add scan-worker clean/rejected/unavailable/indeterminate retry and replacement-independent quarantine state tests in `ContosoDashboard.Tests/Services/DocumentScanWorkerTests.cs`.

### Implementation for User Story 1

- [ ] T012 [P] [US1] Add document enums and `Document` entity in `ContosoDashboard/Models/Document.cs`; enforce these data-model rules verbatim: “Title required ≤255; category in the six values; file ≤25 MB; extension allow-list {.pdf .doc .docx .xls .xlsx .ppt .pptx .txt .jpg .jpeg .png} AND content-type AND magic bytes agree; tags ≤500 chars; project must be one the user is assigned to (or manages) at action time; identity claims (name, email, role, department) all present else deny (REQ-025).” Also implement `ContentType` up to 255, `FileSize` ≤26,214,400, text-stored category max 30, generated blob names, and unique `(UploaderId, UploadToken)`.
- [ ] T013 [P] [US1] Add the append-only `DocumentActivity` entity in `ContosoDashboard/Models/DocumentActivity.cs` with a title snapshot, no document FK, indexed UTC timestamp, and the data-model rule “Detail string(500)? reason (no content, no PII beyond ids)”.
- [ ] T014 [US1] Register document/audit DbSets, relationships, required/max-length constraints, indexes, category string conversion, and upload-token uniqueness in `ContosoDashboard/Data/ApplicationDbContext.cs`.
- [ ] T015 [US1] Create a baseline-compatible EF migration for the existing schema plus the new document and activity tables in `ContosoDashboard/Data/Migrations/`; do not rely on `EnsureCreated()` to evolve an existing database.
- [ ] T016 [P] [US1] Implement private quarantine/clean Azure Blob operations, managed-identity credential use, non-user-derived blob names, promote, stream-read, and delete in `ContosoDashboard/Services/BlobDocumentStorage.cs`.
- [ ] T017 [P] [US1] Implement the Defender for Storage adapter behind `IMalwareScanner`, returning clean, infected, unavailable, or indeterminate outcomes in `ContosoDashboard/Services/DefenderMalwareScanner.cs`.
- [ ] T018 [US1] Implement `DocumentAuditService` writes and upload/rejection/scan-result activity recording in `ContosoDashboard/Services/DocumentAuditService.cs` and `ContosoDashboard/Models/DocumentActivity.cs`.
- [ ] T019 [US1] Implement upload validation, per-file idempotency, quarantine-first persistence, cleanup on DB failure, and pending scan state in `ContosoDashboard/Services/DocumentService.cs`.
- [ ] T020 [US1] Implement the hosted scan worker with retry backoff (1, 5, 15 minutes, capped at 1 hour), quarantine retention for indeterminate results, clean promotion, rejection cleanup, and operator max-age alert logging in `ContosoDashboard/Services/DocumentScanWorker.cs`.
- [ ] T021 [US1] Implement `POST /api/documents` multipart upload with documented 201/400/403/409/413/415 behavior and task/project metadata handling in `ContosoDashboard/Endpoints/DocumentEndpoints.cs`.
- [ ] T022 [US1] Register storage, scanner, service, audit service, hosted worker, request limits, and document endpoints in `ContosoDashboard/Program.cs`.
- [ ] T023 [US1] Build the multi-file upload page/component with title/category requirements, supported-type and size feedback, per-file progress, clear result/retry, and project/task selection in `ContosoDashboard/Pages/Documents.razor` and `ContosoDashboard/Pages/DocumentUpload.razor`.

**Checkpoint**: A clean upload is only available after scan promotion; invalid, failed, pending, or rejected files are inaccessible, and retries/compensation are automatic. This is the MVP increment.

---

## Phase 4: User Story 2 - Organize, browse, and find permitted documents (Priority: P1)

**Goal**: Users browse My, Project, and Shared documents and search/filter/sort only within their current authorized scope.

**Independent Test**: Seed documents across owners, projects, categories, and shares; verify sorting, filters, supported search fields, scope-specific visibility, no count leakage, and target response times for representative employee/project accounts.

### Tests for User Story 2

- [ ] T024 [P] [US2] Add list, filter, sort, search-field, pagination, non-clean exclusion, and authorization-no-leak tests in `ContosoDashboard.Tests/Services/DocumentDiscoveryTests.cs`.
- [ ] T025 [P] [US2] Add authorized/unauthorized download and preview response, header, content-type, and 404 indistinguishability tests in `ContosoDashboard.Tests/Endpoints/DocumentReadEndpointTests.cs`.

### Implementation for User Story 2

- [ ] T026 [P] [US2] Add document query, paging, list-item, and details DTOs with title/date/category/size sorting and category/project/date filters in `ContosoDashboard/Models/DocumentQuery.cs`.
- [ ] T027 [US2] Implement My Documents, Project Documents, Shared with Me, and search queries with authorization predicates applied before counts/results, server-side pagination, and title/description/tags/uploader/project matching in `ContosoDashboard/Services/DocumentService.cs` and `ContosoDashboard/Services/DocumentAuthorizationService.cs`.
- [ ] T028 [US2] Implement authorized download and PDF/JPEG/PNG preview streaming endpoints with request-time reauthorization, identical 404 for unauthorized/missing IDs, audit records, sanitized disposition, `nosniff`, and `private, no-store` headers in `ContosoDashboard/Endpoints/DocumentEndpoints.cs`.
- [ ] T029 [US2] Add My Documents, Project Documents, and Shared with Me tabs plus sorting, filtering, search, empty state, and preview/download actions in `ContosoDashboard/Pages/Documents.razor`.
- [ ] T030 [US2] Add the Documents navigation entry and route access integration in `ContosoDashboard/Shared/NavMenu.razor` and `ContosoDashboard/Program.cs`.

**Checkpoint**: Authorized users can find and open only clean documents they may access; unauthorized content and result counts remain undisclosed.

---

## Phase 5: User Story 3 - Manage and share documents (Priority: P2)

**Goal**: Owners and authorized managers edit metadata, safely replace content, share with employees/departments, revoke access immediately, and confirm permanent deletion.

**Independent Test**: Exercise edit, replacement, share, revocation, notification, and confirmed deletion as owner, team lead, project manager, administrator, and unauthorized user; verify authorization and that a rejected replacement preserves the current clean file.

### Tests for User Story 3

- [ ] T031 [P] [US3] Add management authorization matrix, metadata validation, replacement-keeps-old-file, confirmed deletion, and audit-survival tests in `ContosoDashboard.Tests/Services/DocumentManagementTests.cs`.
- [ ] T032 [P] [US3] Add user/department share grant, immediate revocation, duplicate-active-share, and recipient notification tests in `ContosoDashboard.Tests/Services/DocumentShareTests.cs`.

### Implementation for User Story 3

- [ ] T033 [P] [US3] Add `DocumentShare` model and EF configuration; enforce “TargetUserId int? FK→User when User”, “TargetTeam string(100)? when Team”, “Exactly one of TargetUserId/TargetTeam set (check constraint)”, active uniqueness, and request-time `IsActive` revocation semantics in `ContosoDashboard/Models/DocumentShare.cs` and `ContosoDashboard/Data/ApplicationDbContext.cs`.
- [ ] T034 [US3] Add the share-table migration, including foreign keys, check constraint, and filtered unique active-recipient index, in `ContosoDashboard/Data/Migrations/`.
- [ ] T035 [US3] Implement resource authorization for owner, administrator, project manager, same-department team lead, current project member, and active user/department share scopes in `ContosoDashboard/Services/DocumentAuthorizationService.cs`.
- [ ] T036 [US3] Implement metadata update, replacement staging/scan pointer swap, failed replacement cleanup, and hard delete with retained audit record in `ContosoDashboard/Services/DocumentService.cs` and `ContosoDashboard/Services/DocumentScanWorker.cs`.
- [ ] T037 [US3] Implement user and department share grant/revoke/list behavior, ensuring share recipients receive view/download only and revocation is immediately enforced in `ContosoDashboard/Services/DocumentShareService.cs`.
- [ ] T038 [US3] Extend in-app notification types and implement share-grant and project/member notification dispatch through the existing notification service in `ContosoDashboard/Models/Notification.cs` and `ContosoDashboard/Services/NotificationService.cs`.
- [ ] T039 [US3] Add metadata edit, replace, share/revoke, and explicit-confirmation delete controls with role-appropriate affordances in `ContosoDashboard/Pages/DocumentDetails.razor` and `ContosoDashboard/Pages/Documents.razor`.
- [ ] T040 [US3] Ensure all denied/rejected management actions are audited and unauthorized/missing document identifiers return indistinguishable responses in `ContosoDashboard/Services/DocumentAuditService.cs` and `ContosoDashboard/Endpoints/DocumentEndpoints.cs`.

**Checkpoint**: Authorized managers can maintain and share documents while replacement safety, immediate revocation, hard deletion, and audit retention are preserved.

---

## Phase 6: User Story 4 - Use documents from tasks and the dashboard (Priority: P2)

**Goal**: Users attach/upload task documents, see recent documents and a count on the dashboard, and receive project notices only after scanning succeeds.

**Independent Test**: Attach an existing permitted document and upload one from a task; verify project inheritance, task association, recent-five widget, document count, and post-scan project notification.

### Tests for User Story 4

- [ ] T041 [P] [US4] Add task attach/upload authorization, inherited-project, dashboard recent-five/count, and clean-scan notification tests in `ContosoDashboard.Tests/Services/DocumentTaskDashboardTests.cs`.

### Implementation for User Story 4

- [ ] T042 [US4] Add the `TaskDocument` join entity and composite key/relationships in `ContosoDashboard/Models/TaskDocument.cs` and `ContosoDashboard/Data/ApplicationDbContext.cs`.
- [ ] T043 [US4] Add the task-document migration and ensure document deletion removes task links without leaving stale task views in `ContosoDashboard/Data/Migrations/`.
- [ ] T044 [US4] Implement authorized task attachment/list methods and require task project membership when attaching in `ContosoDashboard/Services/DocumentService.cs`.
- [ ] T045 [US4] Add task document panel for viewing/attaching existing permitted documents and uploading with inherited `Task.ProjectId` in `ContosoDashboard/Pages/Tasks.razor` and `ContosoDashboard/Pages/ProjectDetails.razor`.
- [ ] T046 [US4] Extend `DashboardSummary` with `DocumentCount` and five recent documents, and render the Recent Documents widget and count in `ContosoDashboard/Services/DashboardService.cs`, `ContosoDashboard/Pages/Index.razor`, and `ContosoDashboard/Shared/RecentDocumentsWidget.razor`.
- [ ] T047 [US4] Notify eligible project members only after a new project document reaches `Clean` status in `ContosoDashboard/Services/DocumentScanWorker.cs` and `ContosoDashboard/Services/NotificationService.cs`.

**Checkpoint**: Task and dashboard flows work using the same authorization, scan, and notification rules as the standalone document experience.

---

## Phase 7: User Story 5 - Review document activity (Priority: P3)

**Goal**: Administrators inspect complete activity and generate the three requested reporting views; non-administrators cannot access reports.

**Independent Test**: Perform uploads, downloads, deletions, shares, and denials; verify actor/document/time/action/outcome records and each report as an administrator, then verify a non-administrator is denied.

### Tests for User Story 5

- [ ] T048 [P] [US5] Add append-only audit retention, report aggregation/date-range, and administrator-only access tests in `ContosoDashboard.Tests/Services/DocumentReportingTests.cs` and `ContosoDashboard.Tests/Endpoints/DocumentReportEndpointTests.cs`.

### Implementation for User Story 5

- [ ] T049 [US5] Implement uploads-by-type, top-uploaders, and access-pattern SQL aggregations for a requested period in `ContosoDashboard/Services/DocumentAuditService.cs`.
- [ ] T050 [US5] Add administrator-only `GET /api/admin/document-reports` with supported report types, date validation, and 403 for non-admins in `ContosoDashboard/Endpoints/DocumentEndpoints.cs`.
- [ ] T051 [US5] Add the administrator activity/report page and report-period controls in `ContosoDashboard/Pages/AdminDocumentReports.razor`.
- [ ] T052 [US5] Add configurable audit-retention policy handling, defaulting to no purge when unset, while retaining audit history after document deletion in `ContosoDashboard/Services/DocumentAuditService.cs` and `ContosoDashboard/appsettings.json`.

**Checkpoint**: Administrators can investigate and report; all other users are blocked from administrative reporting.

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Validate feature-wide security, performance, operational readiness, and documented setup.

- [ ] T053 [P] Add an end-to-end authorization and document workflow test using `WebApplicationFactory` in `ContosoDashboard.Tests/EndToEnd/DocumentWorkflowTests.cs`.
- [ ] T054 [P] Add a parameterized peak-concurrency load scenario for list/search and upload targets in `ContosoDashboard.Tests/Performance/DocumentLoadTests.cs`.
- [ ] T055 Enforce TLS 1.3 at the hosting boundary, HTTPS redirect/HSTS, private blob access, and preview-specific framing policy in `ContosoDashboard/Program.cs` and deployment configuration `ContosoDashboard/appsettings.json`.
- [ ] T056 Update the feature quickstart with actual secrets/configuration guidance, migration commands, Azurite setup, test commands, and outstanding peak-load/licensing/retention values in `specs/001-document-upload-management/quickstart.md`.
- [ ] T057 Run the build, `dotnet ef database update`, automated tests, quickstart scenarios, and 25 MB upload / 500-document list / search / preview timing checks; record results and unresolved environment-dependent checks in `specs/001-document-upload-management/quickstart.md`.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies; T001 establishes the solution/test project before package/configuration tasks.
- **Foundational (Phase 2)**: Depends on setup. Identity, authorization contracts, and migration strategy block every story.
- **US1 (Phase 3)**: Depends on Phase 2. It establishes the document storage, scanning, audit, and upload capability used by later increments.
- **US2 (Phase 4)**: Depends on US1's document model, storage, and authorization services.
- **US3 (Phase 5)**: Depends on US1 and US2 shared document/authentication services; sharing and management are still a separately testable increment.
- **US4 (Phase 6)**: Depends on US1 document/upload behavior and US2 read authorization; dashboard/task files can then be implemented independently of US3 UI.
- **US5 (Phase 7)**: Depends on US1 audit records and US3 audit coverage; reporting is independently testable after those actions exist.
- **Polish (Phase 8)**: Depends on all desired stories; external Azure/Entra/peak-load values may remain environment-dependent.

### User Story Dependencies

- **US1 (P1)**: After foundational work; no dependency on another story. MVP.
- **US2 (P1)**: After US1 document persistence, clean-status, and request-time authorization seams.
- **US3 (P2)**: After US1/US2 core document services; independent acceptance coverage for management and sharing.
- **US4 (P2)**: After US1 upload and US2 read authorization. Can proceed in parallel with US3 after shared service contracts are stable, with coordination on `DocumentService.cs` and notification files.
- **US5 (P3)**: After US1 activity storage and US3 actions/audit coverage; may proceed alongside US4 if the shared audit/notification files are coordinated.

### Parallel Opportunities

- In Phase 1, T002 and T003 are independent after the project files are created by T001.
- In Phase 2, T008 can proceed alongside the identity and migration work; T006 depends on the Entra claim shape from T005.
- Within US1, test authoring (T009–T011), model files (T012–T013), and independent Azure adapters (T016–T017) can run concurrently; service integration follows the shared contracts/models.
- Within US2, discovery tests (T024), read endpoint tests (T025), and DTO creation (T026) are parallel; one engineer owns the shared `DocumentEndpoints.cs`/`Documents.razor` integration.
- US3 tests T031–T032 and the share model T033 can be parallel after US1; US4 can run concurrently with US3 after core service contracts stabilize, avoiding simultaneous edits to `DocumentService.cs`, `NotificationService.cs`, and `DocumentScanWorker.cs`.
- US5 reporting tests and report aggregations can be parallel with US4 dashboard work if audit-service changes are coordinated.
- Polish tasks T053 and T054 are independent; final T057 follows all implementation and configuration tasks.

### Parallel Example: User Story 1

```text
Parallel: T009, T010, T011 (tests); T012 and T013 (entities); T016 and T017 (separate adapters).
Then integrate persistence and services: T014 → T015; T018–T022; build the upload UI in T023 against the upload contract.
```

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1 and Phase 2.
2. Implement Phase 3 and verify validation, compensation, clean promotion, quarantine retry, and inaccessible pending/rejected content.
3. Stop at the Phase 3 checkpoint: upload is the MVP; do not require search, sharing, dashboard, or reporting to demonstrate it.

### Incremental Delivery

1. Setup + foundation.
2. US1 secure upload MVP.
3. US2 browse/search and authorized content reads.
4. US3 metadata, replacement, sharing, and deletion.
5. US4 task/dashboard integration.
6. US5 audit reporting.
7. Cross-cutting performance, hosting, and quickstart validation.

### Notes

- Every task is a checkbox with a sequential ID; `[P]` is used only for work on independent files, and story tasks carry the corresponding `[USn]` label.
- Test tasks precede the related implementation tasks within each story; verify them against the implementation as the feature is delivered.
- The constitution file is an unfilled template, not ratified governance. The plan identifies this as an issue; resolve project principles separately before treating it as a gate.
