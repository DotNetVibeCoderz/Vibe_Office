# The API

[← README](../README.md) · [Architecture](architecture.md) · [Configuration](configuration.md) · [Apps](apps.md) · [Mr Clippy](assistant.md) · [Scripts](scripting.md) · [Development](development.md)

`VibeDesk.Api` exposes the domain over three transports — REST, SignalR and gRPC — on top of the same
services the web host calls in-process. **No logic lives in an endpoint.** The moment it does, the
three transports start behaving differently, and only one of them will be the one you tested.

```bash
dotnet run --project src/VibeDesk.Api     # https://localhost:7299
```

Interactive docs at `/scalar/v1` (Development only), the OpenAPI document at `/openapi/v1.json`.
62 REST routes at the time of writing.

---

## Authentication

Two schemes run side by side, and either satisfies the default policy.

**Bearer token** — `POST /api/auth/token` with an email and password returns a JWT.

```bash
curl -X POST https://localhost:7299/api/auth/token \
  -H "Content-Type: application/json" \
  -d '{"email":"fadhil@gravicode.com","password":"VibeDesk#2026"}'
```

Failures are deliberately uninformative: an unknown email and a wrong password produce the same
message, because distinguishing them turns the endpoint into an account-enumeration oracle. An
account with two-factor enabled is **refused** rather than issued a token that skips the second
factor — sign in on the web app and use an API key for automation.

**API key** — send `X-Api-Key`. Keys are created at `POST /api/api-keys`, which returns the plaintext
exactly once; only a SHA-256 hash is stored, so a lost key is replaced rather than recovered.

`Jwt:SigningKey` is empty in configuration on purpose. A committed default would be a signing key
every deployment shares. With none set the host generates an ephemeral one and warns: tokens then
stop working when the process restarts and will not validate on a second instance — fine for
development, and loud enough that nobody ships it by accident.

---

## Shape of the REST surface

| Group | Covers |
|---|---|
| `/api/auth` | Token exchange, `me` |
| `/api/drive` | List, search, folders, documents, upload, download, move, copy, trash, quota |
| `/api/items/{id}` | Content, versions, comments, permissions, link sharing |
| `/api/comments/{id}` | Edit, resolve, accept or reject a suggestion |
| `/api/calendars`, `/api/events` | Calendars, occurrences, invitations, attachments, import, free/busy |
| `/api/users`, `/api/notifications`, `/api/activity`, `/api/sync`, `/api/api-keys` | Platform |
| `/api/assistant` | Mr Clippy: sessions, transcript, settings, streaming replies, attachments |

Conventions worth knowing before you write a client:

- **Enums are names, not ordinals.** `"type": "Spreadsheet"`, not `"type": 2`. A contract that
  exposes ordinals forces every client to hard-code the order of a C# enum.
- **Domain exceptions become status codes** in one place, not per endpoint: `NotFoundException` → 404,
  `ForbiddenException` → 403, `ValidationException` → 400.
- **404 rather than 403 for an item you cannot see.** *Forbidden* would confirm that the id exists.
- **`PUT /api/items/{id}/content` returns 409 with the server's copy** when the stored revision has
  moved on, so the client rebases instead of clobbering a collaborator. `baseRevision: -1` forces the
  write.
- **List endpoints cap `take`.** An unbounded page size is a denial-of-service vector on a shared
  deployment.
- **Downloads are always attachments.** Serving an uploaded SVG or HTML inline would be stored XSS on
  our own origin, and a per-type allow-list is one forgotten entry away from the same bug.

### The assistant streams

`POST /api/assistant/sessions/{id}/messages` replies with **server-sent events** rather than a JSON
array: a reply that takes thirty seconds should start rendering immediately, and SSE survives proxies
that buffer chunked JSON. Each `data:` frame is one `ClippyChunk` — a text delta, a tool-call badge,
or the final marker.

---

## SignalR

`/hubs/collaboration`, one group per Drive item.

| Direction | Name | Purpose |
|---|---|---|
| → | `JoinItem`, `LeaveItem` | Subscribe to an item's change feed |
| → | `Presence` | Broadcast a cursor or selection |
| → | `Typing` | Relay a patch to other editors |
| ← | `ContentChanged` | Someone saved; carries the new revision |
| ← | `CommentsChanged` | A thread was added, resolved or deleted |
| ← | `Presence`, `PresenceJoined`, `PresenceLeft` | Who is here, and where |

**Group membership is the authorisation boundary.** `JoinItem` calls `IPermissionService` before
adding the connection, so a client that has not passed the check never receives another editor's
keystrokes. Presence is not persisted: it is worthless the moment it is stale, and writing it would
put a database round trip in the hot path.

`Typing` is a latency shortcut, never the source of truth. The authoritative save still goes through
`PUT /api/items/{id}/content` with its revision check.

WebSockets cannot carry an `Authorization` header on the handshake, so the hub also accepts the token
as `?access_token=` — scoped to paths under `/hubs`.

---

## gRPC

`Protos/vibedesk.proto`, service `vibedesk.Drive`: `Query`, `Get`, `StreamChildren`, `GetContent`,
`SaveContent`.

`StreamChildren` is server-streaming and pages internally, so a large folder never materialises as one
payload — that is the reason the call exists rather than being a repeated `Query`.

`SaveContent` mirrors the REST 409 as a reply field rather than an `RpcException`: a stale save is an
expected outcome in a collaborative editor, and the client needs the server's copy to rebase on.
`Get` raises `NotFound`, never `PermissionDenied`, for the same reason the REST surface does.

---

## Clients

`VibeDesk.Client` is a ready-made client: one class per Application interface, mapping to these
routes, with an `ApiSession` that owns the token. The desktop and mobile hosts use it, and so can
anything else that wants the contracts rather than raw HTTP. See
[Architecture](architecture.md#layers).
