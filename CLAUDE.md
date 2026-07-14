# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A local, offline RAG (retrieval-augmented generation) assistant for household duties (bills, due dates, sewer emptying, etc.). Facts live in YAML files, are embedded and stored in PostgreSQL + pgvector, and answered by a local Ollama LLM. .NET 10. Notably, the knowledge-base data is in Polish, which drives several retrieval design choices (see Hybrid retrieval below).

## Solution layout (3 projects)

The RAG core is transport-agnostic by design, so it lives in a class library that both front-ends reference:

- **`HomeDutiesAssistant.Core`** — the library: `Models` (incl. `Roles`), `Configuration`, `Infrastructure/{DutiesVector, DutiesSql}`, `Services/{OllamaClient, DataLoader, IngestionService, RagChatService, DutyService}`, **and the canonical `data/*.yaml` facts** (marked `<Content>`, so they flow to any referencing app's output). The assembly is `.Core` but the namespaces remain `HomeDutiesAssistant.*`, so consumers reference the types unchanged.
- **`HomeDutiesAssistant`** — the console front-end: `App` (hosted service), `ConsoleChat`, `Program`.
- **`HomeDutiesAssistant.Web`** — a Blazor Server front-end. On top of the RAG core it adds cookie/JWT **authentication** with three roles, ASP.NET Core **Identity** (EF Core over Postgres), **Serilog→Seq** structured logging, and a **Caddy-fronted Docker** deployment. Pages: `/` (chat), `/manage` (duties CRUD), `/manage/users` (admin), `/login`, `/denied`. Ingestion runs on a Quartz schedule. See "Web front-end", "Authentication & identity", and "Deployment" below.

Both front-ends repeat the same **RAG-core** DI registrations (options, named `HttpClient`, `OllamaClient`/`DutiesVector`/`DataLoader`/`IngestionService`/`RagChatService`) in their own `Program.cs` — there is no shared composition root. If you add/rename a core service, update **both** `Program.cs` files. `DutyService` backs the web-only management UI and is registered **only** in the web `Program.cs`; the web additionally wires a large auth/identity/logging block (DbContext, Identity, JWT, authorization policies, Serilog, forwarded headers, Data Protection) that the console does not have.

## Prerequisites & running

The app depends on two external services that must be up before it runs:

1. **PostgreSQL + pgvector** — the repo-root `docker-compose.yml` is the **full self-hosted stack** (pgvector + web + Caddy + Seq). For local `dotnet run` development bring up **just the database**: `docker compose up -d pgvector`. Exposes `localhost:5432`, db `homeduties`, user `home`. The password comes from `.env` (`POSTGRES_PASSWORD`) — the console's `appsettings.json` hardcodes `home`, so set `POSTGRES_PASSWORD=home` in `.env` for local dev. There is **no `.env.example`** in the repo despite the compose file's header comment referencing one — you must create `.env` (and because Compose interpolates the whole file, even `up -d pgvector` fails unless `JWT_SIGNING_KEY` and `SEQ_ADMIN_PASSWORD` are also set). Data persists in the `pgdata` volume; on **first init of an empty volume**, `db/init/*.sql` creates the Identity tables and seeds an admin user (see "Authentication & identity").
2. **Ollama** — must be running at `http://localhost:11434` with both models pulled: `ollama pull llama3.1:8b` and `ollama pull nomic-embed-text`.

Commands:

```bash
dotnet build HomeDutiesAssistant.sln          # builds all three projects

# Console front-end (from HomeDutiesAssistant/ or pass --project)
dotnet run                 # chat mode (default); auto-ingests if the DB is empty
dotnet run -- ingest       # (re)embed all /data YAML and upsert, then exit
dotnet run -- chat         # explicit chat mode

# Web front-end (from HomeDutiesAssistant.Web/)
dotnet run                 # serves http://localhost:5081 (see Properties/launchSettings.json)
```

There are **no tests** and no linter configured in this repo.

For the **console**, the first CLI arg selects the command (`App.RunAsync`): `ingest` or `chat`/none. Type `exit` to leave chat. Stdin can be piped (non-interactive fallback in `ConsoleChat.ReadQuestion`).

The **web app** requires sign-in (every page except `/login` is `[Authorize]`d) — the seeded admin is **`admin` / `ChangeMe!2026`**. Its Identity tables must already exist (created by `db/init/*.sql` on first DB init — see "Authentication & identity"). Ingestion is not manual — a Quartz job rebuilds the knowledge base on boot and every 6 hours (see Web front-end below).

## Architecture

The RAG core (in `HomeDutiesAssistant.Core`) is transport-agnostic — `RagChatService`/`IngestionService` know nothing about CLI, HTTP, or Blazor. A front-end injects them and decides how to render the streamed answer and persist the conversation.

The RAG pipeline:

- **Ingest** (`IngestionService` ← `DataLoader`): `DataLoader` reads all `*.yaml` under `data/` into `Duty` records. Each `Duty.ToContext()` produces the single canonical sentence that is BOTH embedded and later shown to the LLM as a "fact" — keep these the same so retrieval and answers refer to identical text. `OllamaClient.EmbedDocumentAsync` → 768-dim vector → `DutiesVector.UpsertAsync`.
- **Retrieve + answer** (`RagChatService.AskAsync`): embed the question (`EmbedQueryAsync`) → `DutiesVector.SearchAsync` returns top-K facts → build a system prompt pinning the model to ONLY those facts → `OllamaClient.ChatStreamAsync` streams tokens back. `AskAsync` completes after retrieval and returns a `RagAnswer` whose `Tokens` is a lazy `IAsyncEnumerable` — generation starts only when the caller enumerates it. Both front-ends show a "searching" indicator during retrieval, then stream tokens.

Core component map:
- `Services/OllamaClient.cs` — only external LLM I/O. Embeddings via `POST /api/embed`; chat via `POST /api/chat` with `stream=true` (NDJSON, one JSON object per line). Uses a named `HttpClient` from `IHttpClientFactory` whose base address/timeout (5 min, for cold local models) are set in each front-end's `Program.cs`.
- `Infrastructure/DutiesVector.cs` — executes all PostgreSQL/pgvector access (init, upsert, list, delete, hybrid search, count). `InitializeAsync` creates the `vector` + `pg_trgm` extensions and the `duties` table (idempotent — safe to call repeatedly). The raw SQL strings themselves live in `Infrastructure/DutiesSql.cs`, a static class of `const string` literals — edit queries there, not inline.
- `Services/DutyService.cs` — CRUD over the knowledge base for the management UI: `List`/`Save`/`Delete`. `SaveAsync` recomputes `ToContext()` and re-embeds, so a created/edited duty is queryable by chat immediately (no ingest run needed). Transport-agnostic like the rest of the core.
- `Configuration/Options.cs` — typed options bound from `appsettings.json` (`Ollama`, `Database`, `Rag` sections) via the Options pattern.

### The `duties` key model (changed — keep this straight)

`duties.id` is a **DB-generated `BIGINT GENERATED ALWAYS AS IDENTITY`** surrogate key; on a `Duty`, `Id == 0` means "not yet saved". The **natural/dedupe key is `title` (UNIQUE)**. `DutiesVector.UpsertAsync` branches on `Id`:
- **`Id == 0` (create/YAML seed)** → `INSERT … ON CONFLICT (title) DO UPDATE`, so re-ingesting the same title updates that row instead of duplicating, and the assigned id is written back onto the `Duty`.
- **`Id > 0` (edit)** → `UPDATE … WHERE id = $1`, so a title **can** be renamed. A rename that collides with another row's title raises a unique violation that the UI surfaces.

Deletion exists (`DutyService.DeleteAsync` → `DutiesVector.DeleteAsync` by id). Note ingestion has no orphan-cleanup: removing a duty from the YAML does not delete its row — only the `/manage` UI deletes.

### Console front-end

.NET Generic Host. `Program.cs` wires DI and runs `App` (a `BackgroundService`) which calls `DutiesVector.InitializeAsync`, ingests if the DB is empty, then dispatches to `ConsoleChat` (Spectre.Console UI) or a one-shot ingest.

### Web front-end (`HomeDutiesAssistant.Web`)

Blazor Server. `Components/Pages/Home.razor` (`@rendermode InteractiveServer`, `[Authorize(CanRead)]`) is the chat: it `await foreach`s `RagAnswer.Tokens`, appending each token to a mutable turn and calling `StateHasChanged` so the answer streams into the browser over the SignalR circuit; it also renders the retrieved `Sources`. `Components/Pages/Duties.razor` (`/manage`, `[Authorize(CanManage)]`) is a CRUD UI over `DutyService` — list/create/edit/delete duties; saving re-embeds via `DutyService.SaveAsync`, so the new fact is immediately searchable without waiting for the next Quartz ingest. `Components/Pages/Users.razor` (`/manage/users`, `[Authorize(CanAdmin)]`) manages Identity users via `UserManager`. `Login.razor` and `Denied.razor` use `EmptyLayout` (no chrome).

Ingestion is **scheduled, not manual**: `Jobs/IngestionJob.cs` (Quartz, registered in `Program.cs`) fires once at boot (`StartNow`) and every 6 hours. The job owns the idempotent `DutiesVector.InitializeAsync` call (this creates only the RAG `duties` table + extensions — **not** the Identity tables, which come from `db/init`), so there is no separate startup schema step. `[DisallowConcurrentExecution]` prevents overlapping runs; `IngestionService` is registered **Scoped** to match Quartz's per-execution DI scope (the console registers it Transient).

`Program.cs` also configures **Serilog** (compact JSON to stdout, plus Seq when `Seq:ServerUrl` is set; each request tagged with a request id + signed-in user via `LogContext`), **forwarded headers** (trusts `X-Forwarded-*` from the Caddy proxy so `Request.IsHttps`/scheme are correct behind TLS termination), and **Data Protection** key persistence to `DataProtection:KeyPath` (so antiforgery tokens survive container recreation).

### Authentication & identity (web only)

Auth is **JWT carried in an HttpOnly cookie** — there is no server-side session. Flow: `Login.razor` verifies credentials via `UserManager`, `JwtTokenService.Create` mints an HS256 token embedding a `name` + single `role` claim, and it is written to the `HomeDutiesAssistant.Auth` cookie (`HttpOnly`, `SameSite=Strict`, `Secure` when HTTPS). The `JwtBearer` scheme reads the token *from that cookie* (`OnMessageReceived`); an unauthenticated challenge redirects to `/login`, a forbidden one to `/denied`. Logout is `POST /auth/logout` (deletes the cookie).

- **`CookieJwtAuthenticationStateProvider`** bridges the two Blazor render phases: during SSR/prerender it validates the cookie via `HttpContext`; the resulting claims are saved with `PersistentComponentState` so the interactive circuit (a fresh scope with **no** `HttpContext`) can restore the same principal.
- **Roles** live in `Core` (`Models/Roles.cs`): `Read` ⊂ `Manage` ⊂ `Admin`, hierarchical. A user carries **exactly one** role claim. Policies in `AuthorizationPolicies` (`CanRead`/`CanManage`/`CanAdmin`) each accept that role plus any higher one.
- **Identity storage**: `ApplicationDbContext : IdentityDbContext<ApplicationUser>` with `EFCore.NamingConventions` (**snake_case**) maps the Identity tables (`users`, `roles`, `user_roles`, …). Registered via `AddIdentityCore` (no cookie UI — `RequireUniqueEmail=false`, min password length 8, non-alphanumeric not required).
- **The Identity schema is NOT created by EF migrations** — there are none in the repo. It is created by raw SQL in `db/init/01-identity-schema.sql` (idempotent `IF NOT EXISTS`), which Postgres runs via `docker-entrypoint-initdb.d` **only on first init of an empty `pgdata` volume**. `db/init/02-identity-seed.sql` seeds the three roles and an `admin` user (password `ChangeMe!2026` — change after first login). `DesignTimeDbContextFactory` exists only so `dotnet ef` tooling can scaffold without booting the app; if you add migrations, keep them consistent with `db/init`. **Running the web app against a Postgres that wasn't initialized through compose (e.g. a hand-created dev DB) will have no Identity tables and every login will fail** — run the two SQL files manually in that case.
- **`Auth.Jwt.SigningKey`** must be ≥ 32 bytes. `JwtTokenService`'s constructor throws if it isn't, and `Program.cs` additionally hard-fails at startup in Production if it's missing. Supply it via the `Auth__Jwt__SigningKey` environment variable (compose reads `JWT_SIGNING_KEY` from `.env`); the checked-in `appsettings.json` leaves it empty.

### Deployment (self-hosted stack)

The repo-root `docker-compose.yml` runs four services: **pgvector** (Postgres 17 + the `db/init` mount), **web** (built from the multi-stage `Dockerfile`), **caddy** (TLS termination + reverse proxy, config in `Caddyfile`, driven by `SITE_ADDRESS`), and **seq** (log UI). Bring it all up with `docker compose up -d --build` from the repo root. The web container reaches Ollama **outside** the stack at `host.docker.internal:11434` (host-installed Ollama must listen on `0.0.0.0`). All secrets/ports come from `.env`: `POSTGRES_PASSWORD`, `JWT_SIGNING_KEY`, `SEQ_ADMIN_PASSWORD`, `SITE_ADDRESS`, and optional `HTTP_PORT`/`HTTPS_PORT`/`SEQ_UI_PORT`. The `Dockerfile` deliberately does `dotnet publish` in one step (no split restore) — a separately-cached restore leaves Blazor's static-web-asset processing incomplete and silently breaks interactivity.

## Hybrid retrieval (the non-obvious part)

`DutiesVector.SearchAsync` fuses two rankings with Reciprocal Rank Fusion (RRF):

```
score = LexicalWeight/(RrfK + lexicalRank) + VectorWeight/(RrfK + vectorRank)
```

- **lexical** rank: `pg_trgm` `word_similarity` over category (weight 0.6) + content (0.4).
- **vector** rank: pgvector cosine distance (`<=>`).

RRF ranks by *position*, letting two signals on different scales combine. Defaults in `RagOptions`: `LexicalWeight=2.0`, `VectorWeight=1.0`, `RrfK=60`. Lexical is weighted higher **because `nomic-embed-text` is weak on Polish**, and the data is Polish. If you swap in a strong multilingual embedding model, raise `VectorWeight` relative to `LexicalWeight`.

## Important gotchas

- **`nomic-embed-text` requires task prefixes.** Documents are embedded with `search_document: ` and queries with `search_query: ` (`OllamaOptions.EmbedDocumentPrefix`/`EmbedQueryPrefix`) so both land in the same vector space. If you change the embedding model, update these (set to `""` for models needing no prefix) and `EmbeddingDimensions` (currently 768) — which must match the `vector(N)` column. Changing dimensions requires recreating the `duties` table.
- **`data/*.yaml` now lives in the `HomeDutiesAssistant.Core` project** (`<Content Include="data\**\*.yaml">`) and is the single source of truth. Both front-ends receive the files in their build output via the project reference — the web project no longer links its own copy. After editing the YAML: the console needs an explicit `dotnet run -- ingest`; the web app re-ingests automatically on its next Quartz run (or restart). Records upsert by `title` (see "The `duties` key model" above); renaming a `title` in YAML inserts a new row and leaves the old one behind (ingestion has no orphan cleanup — only the `/manage` UI deletes).
- Each project's `appsettings.json` and the Core `data/**/*.yaml` land in the build output (`CopyToOutputDirectory`); the app reads `data/` from `AppContext.BaseDirectory`, not the source tree. There are **two** `appsettings.json` files (console + web) — keep the `Ollama`/`Database`/`Rag` sections in sync. The web one adds `Serilog` and `Auth` sections (console has neither).
- DI registrations for the RAG core are duplicated across the two `Program.cs` files (no shared composition root) — a new/renamed core service must be registered in both. The web `Program.cs` additionally owns all the auth/identity/logging/proxy wiring, which the console does not have.
- Adding a field to `Duty` touches several places, because the structured fields are persisted as their own columns (the DB is the source of truth the `/manage` UI reads back): append it in `ToContext()` (or it won't be embedded or seen by the model); add the column to `DutiesSql.CreateTable`, `Insert`, `Update`, and `List`; bind it in `DutiesVector.AddDutyParameters` and read it in `ListDutiesAsync`; and add an input to `Duties.razor`. Adding a column requires recreating (or migrating) the `duties` table — there are no migrations.