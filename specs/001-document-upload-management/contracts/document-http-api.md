# Contract: Document HTTP Endpoints

Authenticated (Entra ID cookie session) minimal-API endpoints in `ContosoDashboard/Endpoints/DocumentEndpoints.cs`. Used for streaming content; list/search/manage UI calls `IDocumentService` directly from Blazor. Every endpoint re-evaluates authorization per request (REQ-022). Unauthorized **and** nonexistent ids both return `404` with an identical body.

## Common
- Auth: required; missing identity claims → `403` with message (REQ-025). Unauthenticated → `401`/redirect.
- Errors: `application/problem+json`.
- Response headers on content: `X-Content-Type-Options: nosniff`, `Cache-Control: private, no-store`.
- All calls produce a `DocumentActivity` row (success and security-relevant denial).

## POST /api/documents  (multipart/form-data)
Fields: `file` (1 per request; client sends multiple in parallel), `title` (req), `category` (req, one of six), `description`, `projectId`, `taskId`, `tags`, `uploadToken` (guid, idempotency).
- `201` `{documentId, scanStatus:"Pending"}` — content not accessible until Clean.
- `400` invalid metadata; `413` > 25 MB; `415` unsupported type/signature mismatch; `403` not assigned to project; `409` duplicate uploadToken with different payload. Same token+payload returns the original `201` (retry-safe).

## PUT /api/documents/{id}/file  (replace)
Multipart `file`. `202` accepted (replacement pending scan; current file remains served). `404/403/413/415` as above.

## GET /api/documents/{id}/download
`200` stream, `Content-Disposition: attachment; filename*=` from sanitized title+extension. `404` if not Clean/not authorized.

## GET /api/documents/{id}/preview
PDF/JPEG/PNG only, `Content-Disposition: inline`; `415` for other types (UI falls back to download). Target ≤3 s.

## Admin reports  GET /api/admin/document-reports?type=uploads-by-type|top-uploaders|access-patterns&from=&to=
Administrator only; non-admin `403`. `200` `{ period, rows:[...] }`.

## Rules table
| Action | Allowed |
|---|---|
| View/download | Administrator; owner; active share (user/team); project member (Clean only) |
| Edit/replace/share/revoke/delete | Owner; Project Manager of document's project; Team Lead for same-Department owner; Administrator |
| Upload to project | Assigned member or its manager; personal upload always |
| Reports | Administrator |
