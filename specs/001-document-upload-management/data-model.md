# Data Model: Document Upload and Management

EF Core entities added to `ContosoDashboard/Models/` and registered in `ApplicationDbContext`. Ids are `int` (REQ-024), consistent with `User`/`Project`/`TaskItem`.

## Enums (stored as strings for categories per REQ-024)

- `DocumentCategory`: ProjectDocuments, TeamResources, PersonalFiles, Reports, Presentations, Other — stored via `HasConversion<string>()` (max 30).
- `ScanStatus`: Pending, Clean, Rejected.
- `ShareTargetType`: User, Team (team = `User.Department` value).
- `DocumentAction`: Upload, Download, Preview, Delete, ShareGrant, ShareRevoke, Replace, EditMetadata, Denied, ScanResult.
- `ActivityOutcome`: Success, Failure, Denied.
- Extend `NotificationType` with `DocumentShared`, `DocumentAdded`.

## Document

| Field | Type | Rules |
|---|---|---|
| DocumentId | int PK | identity |
| Title | string(255) | required |
| Description | string(2000)? | optional |
| Category | DocumentCategory | required, string-stored |
| Tags | string(500)? | optional, normalized comma list (also searched) |
| ProjectId | int? FK→Project | optional; SetNull on project delete is NOT used — Restrict |
| UploaderId | int FK→User | required, Restrict |
| OriginalFileName | string(255) | display only; never used for storage |
| ContentType | string(255) | REQ-024 |
| FileSize | long | ≤ 26,214,400 |
| BlobName | string(200) | `{documentId}/{guid}`; current clean blob; null until Clean |
| ScanStatus | ScanStatus | Pending on create |
| ScanAttemptCount | int | retry counter |
| NextScanAttemptUtc | DateTime? | backoff scheduling |
| PendingBlobName / PendingFileName / PendingContentType / PendingFileSize / PendingScanStatus | nullable | replacement in flight (REQ-011) |
| UploadDateUtc | DateTime | default UtcNow |
| UpdatedDateUtc | DateTime | |
| UploadToken | Guid | unique with UploaderId; idempotent retry (REQ-023) |

Indexes: (UploaderId, UploadDateUtc desc), (ProjectId, ScanStatus), (Category), (ScanStatus, NextScanAttemptUtc), unique (UploaderId, UploadToken), Title.

State transitions: `Pending → Clean` (promote blob, notify project members) | `Pending → Rejected` (blob deleted; record retained as rejected for uploader notice, hidden from everyone else) | indeterminate/unavailable → remains `Pending`, rescheduled. Replacement: `PendingScanStatus Pending → Clean` (swap pointers, delete old blob) | `Rejected` (clear Pending* fields). Delete: row and blob hard-removed; audit rows persist.

Visibility rule: content/metadata is exposed to non-uploader users only when `ScanStatus == Clean`.

## DocumentShare

| Field | Type | Rules |
|---|---|---|
| ShareId | int PK | |
| DocumentId | int FK→Document | Cascade on document delete |
| TargetType | ShareTargetType | |
| TargetUserId | int? FK→User | when User |
| TargetTeam | string(100)? | when Team (Department) |
| GrantedById | int FK→User | |
| CreatedDateUtc | DateTime | |
| IsActive | bool | false once revoked |
| RevokedDateUtc / RevokedById | nullable | |

Access = view/download only. Unique filtered index on (DocumentId, TargetType, TargetUserId/TargetTeam) where IsActive. Exactly one of TargetUserId/TargetTeam set (check constraint). Revocation sets IsActive=false; authorization reads this at request time → immediate.

## TaskDocument (task↔document link, REQ-014)

TaskId FK→TaskItem, DocumentId FK→Document (composite PK), AttachedById, AttachedDateUtc. Cascade from both parents (deleting a document removes links; task views stay consistent).

## DocumentActivity (audit, REQ-019/020)

| Field | Type | Rules |
|---|---|---|
| ActivityId | long PK | |
| DocumentId | int? | **no FK** — survives deletion |
| DocumentTitle | string(255) | snapshot |
| ActorUserId | int? | null for system (scan worker) |
| Action | DocumentAction | |
| Outcome | ActivityOutcome | |
| Detail | string(500)? | reason (no content, no PII beyond ids) |
| TimestampUtc | DateTime | indexed |

Append-only (no update/delete in app code). Retention per Contoso records policy (config `Documents:AuditRetentionDays`, unset = keep).

## Relationships to existing entities

- User 1—* Document (uploader); User 1—* DocumentShare (target/granted).
- Project 1—* Document (optional); TaskItem *—* Document via TaskDocument.
- Notification: unchanged schema; new types only.
- `DashboardSummary` (service DTO) gains `DocumentCount` and `RecentDocuments` (5 × {Id, Title, Category, UploadDate}).

## Validation rules

Title required ≤255; category in the six values; file ≤25 MB; extension allow-list {.pdf .doc .docx .xls .xlsx .ppt .pptx .txt .jpg .jpeg .png} AND content-type AND magic bytes agree; tags ≤500 chars; project must be one the user is assigned to (or manages) at action time; identity claims (name, email, role, department) all present else deny (REQ-025).
