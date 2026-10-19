# Phase 0 Research: Document Upload and Management

No `NEEDS CLARIFICATION` markers remain; spec clarifications (cloud-only, quarantine+retry, immediate revocation, audit retention per policy, inherited availability targets) are applied. Findings below come from the spec and the existing code (`ContosoDashboard/`).

## Existing-code findings (constraints on design)

- .NET 10 Blazor Server + Razor Pages, EF Core SqlServer, `EnsureCreated()` at startup (no migrations). Scoped services `I*Service`/`*Service` in `Services/`.
- Auth today is a **mock cookie login** (`Pages/Login.cshtml.cs`, pick a seeded user); `Microsoft.Identity.Web`/`OpenIdConnect` packages are referenced but unused. Roles come from `User.Role` (Employee, TeamLead, ProjectManager, Administrator).
- There is **no Team entity**. Team scope can only be derived from `User.Department`. Project scope: `Project.ProjectManagerId` + `ProjectMember`.
- Ids are `int` (User, Project, TaskItem, Notification). `NotificationType` is an enum; `Notification` has no link/target field.
- `DashboardSummary` has 4 counters (no document count). No test project, no solution file.

## Decisions

### D1 Entra ID sign-in (REQ-001, REQ-025)
- Decision: Add OpenID Connect (Microsoft.Identity.Web, already referenced) as the challenge scheme with cookie sign-in; on `OnTokenValidated`, map the Entra identity (email) to the local `User` row and issue claims: UserId, name, email, role, department. If any is missing, deny with a clear message. Keep the mock login only behind a Development-only flag (not used for this feature's acceptance).
- Rationale: Spec says Entra ID, cloud-only; local `User` remains source of role/team/project.
- Alternatives: Entra app roles/groups for roles (rejected: spec says dashboard assignments are the source); keep mock auth (rejected by clarification).

### D2 Storage: Azure Blob behind an abstraction (REQ-018, REQ-022, REQ-023)
- Decision: `IDocumentStorage` (Put/OpenRead/Delete/Promote) implemented with `Azure.Storage.Blobs` and `DefaultAzureCredential` (managed identity). A **private** container, public access disabled, no SAS handed to browsers; downloads/previews stream through an authorized server endpoint. Blob name = `{documentId}/{fileGuid}` (never user-derived). Encryption at rest is Azure-default (SSE); TLS 1.3 enforced via App Service/Front Door minimum TLS setting plus HTTPS redirect/HSTS (already present).
- Two containers (or prefixes): `quarantine` (new/unscanned) and `documents` (clean). Promotion occurs only after a clean scan.
- Rationale: Meets "not reachable by URL", hostile filenames, and swappable backend.
- Alternatives: SAS URLs (rejected: bypass re-check at request time, REQ-022); local disk (out of scope).

### D3 Malware scanning with quarantine and automatic retry (REQ-006)
- Decision: `IMalwareScanner` abstraction; default implementation uses **Microsoft Defender for Storage malware scanning** (on-upload scan of the quarantine container, results via blob index tags / Event Grid). A hosted `BackgroundService` (`DocumentScanWorker`) polls/consumes results and drives `ScanStatus`: `Pending → Clean | Rejected`; unavailable/indeterminate → stays `Pending`, retried with backoff (e.g., 1, 5, 15 min, capped 1 h) until a definitive result; a max-age alert is raised for operators. 
- Rationale: Native Azure, no scanning engine to host; the abstraction keeps tests deterministic (fake scanner).
- Alternatives: ClamAV container (more ops burden; viable fallback behind same interface); in-process scan (rejected: not robust).
- Open item for ops (not blocking the plan): confirm Defender for Storage is licensed for the subscription; otherwise use the ClamAV adapter.

### D4 Upload flow & consistency (REQ-003, REQ-023)
- Decision: Blazor `InputFile` with `OpenReadStream(maxAllowedSize: 25 MB)`, per-file progress via a wrapped stream, files processed independently. Order: validate (extension allow-list + content-type + magic-byte sniff, size) → write blob to quarantine → insert `Document` row (`Pending`) in one DB transaction → on DB failure delete blob; a reconciler job removes orphan blobs/stale rows. Row is created only after the blob exists, so no record points to missing content. Retry is idempotent per client-generated upload token.
- Blazor Server circuit/SignalR message limit is raised (`HubOptions.MaximumReceiveMessageSize`) modestly; large streaming via `InputFile` chunks is already handled. A Minimal-API multipart endpoint (`POST /api/documents`) is the alternative if 30 s/25 MB isn't met under test.
- Alternatives: direct-to-blob browser upload with SAS (rejected, see D2).

### D4b Replacement (REQ-011)
- Decision: Replacement writes to quarantine with a `PendingReplacement*` set on the document; current clean file stays served until the replacement is `Clean`, then pointers swap atomically and old blob is deleted. Rejected replacement leaves current file unchanged.

### D5 Authorization (REQ-002, REQ-013, REQ-022, REQ-025)
- Decision: One `IDocumentAuthorizationService` (resource-based) evaluates every view/preview/download/edit/replace/share/delete at request time from DB state: Admin → all; owner; active share (user or team); project member (view/download); project manager of document's project (manage); Team Lead (manage) for documents whose **owner's Department equals the lead's Department** (Department used as team, since no Team entity exists — see Risk R1). Unauthorized and nonexistent ids both return 404.
- "Team" share recipient = Department value; membership evaluated live.
- Rationale: Single choke point; testable matrix.

### D6 Search & list (REQ-007–009, REQ-017)
- Decision: Authorization predicate is composed into the EF query (`IQueryable<Document>` scoped by user) so counts/results never leak. Search uses indexed columns + `LIKE`/`CONTAINS` on Title, Description, Tags (normalized text column), uploader name, project name; add SQL Server full-text index if the 500-doc / 2 s target is not met at seed volume. Pagination server-side.
- Alternatives: Azure AI Search (rejected: over-engineered for target volumes).

### D7 Preview/download (REQ-010)
- Decision: `GET /api/documents/{id}/download` and `/preview` (authorize, audit, stream; `Content-Disposition` fixed server-side from sanitized title, `X-Content-Type-Options: nosniff`). Preview only for PDF/JPEG/PNG (inline); others offer download. Existing CSP `img-src` and `frame-ancestors`/X-Frame-Options DENY need `SAMEORIGIN` for the preview route only.

### D8 Audit & reporting (REQ-019, REQ-020)
- Decision: Append-only `DocumentActivity` table with **no FK cascade** from Document (stores DocumentId + title snapshot), so rows survive deletion. Retention: enforced by a configurable retention period from Contoso's records-retention policy (value to be supplied; default no purge). Reports (types, top uploaders, access patterns by period) are SQL aggregations restricted by the `Administrator` policy.

### D9 Notifications (REQ-013, REQ-016)
- Decision: Reuse `INotificationService`; add `NotificationType` values `DocumentShared`, `DocumentAdded`. Project-added notices fire only when scan becomes `Clean`; share notices fire on share creation. Add nullable `DocumentId` to Notification is **not** needed initially (message text only); revisit for deep-linking.

### D10 Dashboard/Task integration (REQ-014, REQ-015)
- Decision: Add `RecentDocuments` (5) and `DocumentCount` to `DashboardSummary`; new `RecentDocuments` component on `Index.razor`. A `TaskDocument` join table links documents to tasks; tasks page gets a documents panel (attach existing / upload new inheriting `Task.ProjectId`).

### D11 Schema evolution
- Decision: Because the app uses `EnsureCreated()`, new tables will not appear in an existing DB. Plan introduces EF migrations (initial baseline + document tables) and replaces `EnsureCreated` outside Development.

### D12 Testing
- Decision: Add `ContosoDashboard.Tests` (xUnit, EF Core InMemory/SQLite for service tests, `WebApplicationFactory` for endpoint tests, Azurite for blob integration, fake `IMalwareScanner`). Authorization matrix and quarantine/retry state machine get the most coverage. No test project exists today, so this is new.

### D13 Availability / load (REQ-021)
- Decision: Targets are inherited and unspecified in the repo; plan includes a load test (k6/NBomber) at a parameterized concurrency. Stateless scan worker + blob storage allow scale-out; Blazor Server needs sticky sessions/Azure SignalR if scaled out (note for hosting).

## Risks / Open Items

- R1: No Team entity — Department stands in for "team"; confirm with stakeholders or add a Team model later.
- R2: Peak-load and availability numbers are "inherited" but not documented in the repo; need values for load test.
- R3: Records-retention period for audit logs must be provided.
- R4: Defender for Storage licensing/region availability.
- R5: Migrating mock login to Entra requires an app registration and mapping policy for users not in `Users`.
