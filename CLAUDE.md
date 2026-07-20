# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A local, offline RAG (retrieval-augmented generation) assistant for household duties (bills, due dates, sewer emptying, etc.). Facts live in YAML files, are embedded and stored in PostgreSQL + pgvector, and answered by a local Ollama LLM. .NET 10. The bundled knowledge base is in Polish, which drives several retrieval choices (see Hybrid retrieval).

The web front-end is **multi-tenant**: every user belongs to exactly one **Home**, and all data (duties, tasks, chat retrieval) is scoped to that Home. Users **self-register** (creating their Home) and there is a per-home admin role plus a global super-admin. The console front-end is single-tenant and operates against a default Home.

## Solution layout (3 projects)

The RAG core is transport-agnostic by design, so it lives in a class library that both front-ends reference:

- **`HomeDutiesAssistant.Core`** — the library:
  - `Models`: `Duty`, `Home` (+ `HomeLimits`), `Roles`, `Task` (+ `TaskStatus`).
  - `Configuration/Options.cs`, `Infrastructure/{DutiesRepository, DutiesSql, HomesRepository, HomesSql, TasksRepository, TasksSql}`, `Services/{OllamaClient, DataLoader, IngestionService, RagChatService, DutyService, TaskService, HomeService}`.
  - **the canonical `data/*.yaml` facts** (marked `<Content>`, so they flow to any referencing app's output).
  - The assembly is `.Core` but namespaces remain `HomeDutiesAssistant.*`.
- **`HomeDutiesAssistant`** — the console front-end: `App` (hosted service), `Services/ConsoleChat`, `Program`. Single-tenant (default Home).
- **`HomeDutiesAssistant.Web`** — a Blazor Server front-end. On top of the RAG core it adds cookie/JWT **authentication** with four roles + **self-registration**, ASP.NET Core **Identity** (EF Core over Postgres), **Serilog→Seq** logging, and a **Caddy-fronted Docker** deployment. Pages: `/` (chat), `/tasks`, `/manage` (duties), `/manage/users`, `/manage/homes`, `/register`, `/confirm`, `/login`, `/denied`. Ingestion runs on a Quartz schedule.

Both front-ends repeat the RAG-core DI registrations (options, named `HttpClient`, `OllamaClient`/`DutiesRepository`/`HomesRepository`/`HomeService`/`DutyService`/`DataLoader`/`IngestionService`/`RagChatService`) in their own `Program.cs` — **no shared composition root**, so a new/renamed core service must be registered in both. `TaskService`/`TasksRepository` are web-only. The web additionally wires a large auth/identity/logging block the console lacks.

## Database schema — ALL in `db/init` SQL (important)

**The app performs no DDL at runtime.** All schema is raw SQL under `db/init/`, run by Postgres via `docker-entrypoint-initdb.d` **only on first init of an empty `pgdata` volume** (alphabetical order):

1. `01-identity-schema.sql` — `CREATE SCHEMA identity` + the ASP.NET Core Identity tables (`identity.users`, `identity.roles`, `identity.user_roles`, `identity.user_claims`, `identity.role_claims`, `identity.user_logins`, `identity.user_tokens`).
2. `02-identity-seed.sql` — the four roles (`Read`, `Manage`, `HomeAdmin`, `Admin`) and the seeded `admin` user (`Admin` role, password `ChangeMe!2026`).
3. `03-home-schema.sql` — the `vector` + `pg_trgm` extensions (installed into **`public`** so their types/operators resolve via the default `search_path`), `CREATE SCHEMA home`, and the domain tables `home.homes`, `home.duties`, `home.tasks`, `home.user_homes`.
4. `04-home-seed.sql` — the default `Home` and the `admin` user's membership in it.

Two Postgres schemas: **`identity`** (auth) and **`home`** (domain). `public` holds only the extensions. The SQL constant files qualify every table (`home.duties`, `home.tasks`, `home.homes`, `home.user_homes`); Identity access qualifies via EF's `HasDefaultSchema("identity")` in `ApplicationDbContext`.

**Consequences:**
- **Schema changes require recreating the volume** — `CREATE TABLE IF NOT EXISTS` won't re-run against an existing volume, and there are no migrations. Reset with `docker compose down -v && docker compose up -d --build`.
- **A hand-created DB not initialized through compose will be missing all schema** and every operation fails — run the four `db/init/*.sql` files manually there.
- If a `db/init` script has a **syntax error**, `ON_ERROR_STOP` aborts the whole init after the last good file, leaving a partial DB (this actually happened once with a stray comma in the seed). Verify SQL edits against a throwaway container before relying on them.

## Prerequisites & running

Two external services must be up:

1. **PostgreSQL + pgvector** — the repo-root `docker-compose.yml` is the **full self-hosted stack** (pgvector + web + Caddy + Seq). For local `dotnet run` bring up just the DB: `docker compose up -d pgvector` (it mounts `db/init`, so the fresh volume gets the full schema + seed). Exposes `localhost:5432`, db `homeduties`, user `home`. Password comes from `.env` (`POSTGRES_PASSWORD`); the console's `appsettings.json` hardcodes `home`. There is **no `.env.example`**; Compose interpolates the whole file, so even `up -d pgvector` fails unless `JWT_SIGNING_KEY` and `SEQ_ADMIN_PASSWORD` are also set.
2. **Ollama** — at `http://localhost:11434` with `ollama pull llama3.1:8b` and `ollama pull nomic-embed-text`.

```bash
dotnet build HomeDutiesAssistant.sln          # builds all three projects

# Console (from HomeDutiesAssistant/)
dotnet run                 # chat (default); auto-ingests into the default Home if empty
dotnet run -- ingest       # (re)embed all data/*.yaml into the default Home, then exit
dotnet run -- chat         # explicit chat

# Web (from HomeDutiesAssistant.Web/)
dotnet run                 # http://localhost:5081 (see Properties/launchSettings.json)
```

No tests, no linter. Every web page except `/login`, `/register`, `/denied` is `[Authorize]`d. Seeded super-admin: **`admin` / `ChangeMe!2026`**; new users self-register at `/register`. Ingestion is scheduled (Quartz), not manual.

## Architecture

The RAG core is transport-agnostic — `RagChatService`/`IngestionService` know nothing about CLI, HTTP, or Blazor.

RAG pipeline (all home-scoped):
- **Ingest** (`IngestionService` ← `DataLoader`): reads `*.yaml` under `data/` into `Duty` records, stamps each with a `HomeId`, `Duty.ToContext()` produces the single canonical sentence that is BOTH embedded and shown to the LLM, `OllamaClient.EmbedDocumentAsync` → 768-dim vector → `DutiesRepository.SaveAsync`.
- **Retrieve + answer** (`RagChatService.AskAsync(question, history, homeId, ct)`): embed the question → `DutiesRepository.SearchAsync(..., homeId)` returns top-K facts **from that home only** → system prompt pinned to ONLY those facts → `OllamaClient.ChatStreamAsync` streams tokens. `AskAsync` returns a `RagAnswer` whose `Tokens` is a lazy `IAsyncEnumerable` — generation starts when the caller enumerates it.

Core component map:
- `Services/OllamaClient.cs` — only external LLM I/O. Embeddings via `POST /api/embed`; chat via `POST /api/chat` (`stream=true`, NDJSON). Named `HttpClient` (base address/5-min timeout set in each `Program.cs`).
- `Infrastructure/DutiesRepository.cs` — all pgvector access (save, list, delete, hybrid search, count), **home-scoped**. No DDL. SQL literals live in `Infrastructure/DutiesSql.cs` — edit queries there.
- `Infrastructure/HomesRepository.cs` (+ `HomesSql`) — homes CRUD and `user_homes` membership (`SetUserHomeAsync`, `GetUserHomeAsync`, `ListUserIdsAsync`).
- `Infrastructure/TasksRepository.cs` (+ `TasksSql`) — home-scoped task CRUD + drag-and-drop reorder. No embeddings.
- `Services/DutyService.cs` — duty CRUD for `/manage`; `SaveAsync` re-embeds and enforces the per-home `HomeLimits.MaxDuties` (1000).
- `Services/TaskService.cs` — task CRUD/reorder; enforces `HomeLimits.MaxTasks` (1000).
- `Services/HomeService.cs` — home create/list, `GetDefaultAsync` (console + first-run), `AssignAsync`/`GetUserHomeAsync`/`ListUserIdsAsync` (membership).
- `Configuration/Options.cs` — typed options (`Ollama`, `Database`, `Rag`).

### Multi-tenancy: the Home model

- Every `Home` (`home.homes`) owns its duties, tasks, and members. `home.duties` and `home.tasks` carry a `home_id` FK (cascade). Duty uniqueness is **per home**: `UNIQUE (home_id, title)`.
- **`home.user_homes`** maps users→homes: `PRIMARY KEY (user_id)` → **one home per user** (no switching); `home_id` is non-unique → **many users per home**. It lives in the `home` schema (FK `home → identity.users`), keeping `identity` free of any `home` dependency.
- The signed-in user's home id rides in the JWT **`home` claim** (see Auth). Pages read it with `JwtTokenService.HomeId(principal)` (a `long?`) via a cascading `Task<AuthenticationState>`, and pass it into the core services. The console uses `HomeService.GetDefaultAsync()` (the default `Home`).
- `DutiesRepository.SaveAsync` branches on `Duty.Id`: `Id == 0` → `INSERT … ON CONFLICT (home_id, title) DO UPDATE` (re-ingest/create dedupes within the home); `Id > 0` → `UPDATE … WHERE id = $1 AND home_id = $2`. Deletion is by `(id, home_id)`. Ingestion has no orphan cleanup — only the `/manage` UI deletes.

### Console front-end

.NET Generic Host. `Program.cs` wires DI; `App` (a `BackgroundService`) resolves the default Home via `HomeService.GetDefaultAsync` (this first DB call **is** the connectivity check — no DDL), ingests into it if empty, then dispatches to `ConsoleChat` (Spectre.Console) or a one-shot ingest, threading the home id through.

### Web front-end (`HomeDutiesAssistant.Web`)

Blazor Server. Pages read the current home from the JWT `home` claim (cascading `AuthenticationState` + `JwtTokenService.HomeId`) and scope every core call to it:

- `Home.razor` (`/`, `[Authorize(CanRead)]`) — chat: `await foreach`s `RagAnswer.Tokens` into a mutable turn, renders `Sources`.
- `Tasks.razor` (`/tasks`, `[Authorize(CanManage)]`) — task list with optional due date + status, and **HTML5 drag-and-drop reordering** (no JS library). Reorder persists the full new order (see Tasks below).
- `Duties.razor` (`/manage`, `[Authorize(CanManage)]`) — duty CRUD over `DutyService`; saving re-embeds immediately.
- `Users.razor` (`/manage/users`, `[Authorize(CanAdminHome)]`) — member management, **scoped**: a `HomeAdmin` sees/manages only their home's members (create member, change role up to `HomeAdmin`, delete) and cannot touch a global `Admin`; an `Admin` sees all users with each one's home and can pick any home/role. Members created here are **confirmed/active immediately**; pending founders show **· pending** with an **Approve** action (the admin-approval mechanism, or a manual override). Last-admin guards prevent demoting/deleting the final `Admin`.
- `Homes.razor` (`/manage/homes`, `[Authorize(CanAdmin)]`) — **super-admin only**: list all homes with their members, create a home, delete a whole home (deletes every member account, then the home — the FK cascade drops that home's duties/tasks/memberships), or delete a single user. Guards block deleting your own home or your own account (so the last global `Admin` can't be removed). Its private `Home`/`User` records win name resolution over the `Home` model and `Home.razor` page class.
- `Register.razor` (`/register`, `[AllowAnonymous]`) — public self-registration: creates the founding user (**unconfirmed**), assigns `HomeAdmin`, creates a **new** home, writes the membership. **No auto-login.** In email mode it collects an email (rejects duplicates) and sends a `/confirm` link; in approval mode it shows "awaiting an administrator's approval." Rolls the account back if the home name is taken.
- `Confirm.razor` (`/confirm`, `[AllowAnonymous]`) — validates the emailed token (`ConfirmEmailAsync`) to confirm a founder's account.
- `Login.razor` / `Denied.razor` use `EmptyLayout`. Login is blocked while `email_confirmed == false`.

Ingestion is scheduled: `Jobs/IngestionJob.cs` (Quartz) fires at boot (`StartNow`) and every 6 hours; it does **no DDL** — it resolves the default Home and seeds the YAML into it only when empty. `[DisallowConcurrentExecution]`; `IngestionService` is Scoped in web (Transient in console). `Program.cs` also configures **Serilog** (compact JSON + Seq), **forwarded headers** (trust Caddy's `X-Forwarded-*`), and **Data Protection** key persistence.

### Tasks (web-only)

- `Models/Task.cs` — `Task` (+ `TaskStatus` enum: `Todo`/`InProgress`/`Done`) in `HomeDutiesAssistant.Models`. Fields include `HomeId`, optional `DueDate` (`DateOnly?`), and `Priority` (manual drag order). **`Task` collides with `System.Threading.Tasks.Task`** — see gotchas.
- Reorder uses a **full rewrite**: `TasksSql.Reorder` sets each row's `priority` to its 1-based position via `unnest($2::bigint[]) WITH ORDINALITY` (the page sends the reordered id list). Simple and correct at household scale; O(N) per move (fine here). Fractional indexing would be the O(1) alternative if this ever needed scale.

### Authentication, roles & registration (web only)

Auth is a **JWT in an HttpOnly cookie** (`HomeDutiesAssistant.Auth`) — no server session. `JwtTokenService.Create(userName, role, homeId?)` mints an HS256 token with `name` + single `role` + optional `home` claim; login/registration write it to the cookie (`HttpOnly`, `SameSite=Strict`, `Secure` when HTTPS). The `JwtBearer` scheme reads it from the cookie (`OnMessageReceived`); challenge → `/login`, forbidden → `/denied`. Logout is `POST /auth/logout`. `JwtTokenService.HomeId(principal)` is the static helper pages use to read the `home` claim.

- **`CookieJwtAuthenticationStateProvider`** bridges SSR/prerender (validates the cookie via `HttpContext`) and the interactive circuit (restores the principal from `PersistentComponentState`, so the `home` claim survives).
- **Roles** (`Core/Models/Roles.cs`): hierarchical `Read ⊂ Manage ⊂ HomeAdmin ⊂ Admin` (each user holds exactly one role; policies accept that role plus any higher one). `Read`/`Manage`/`HomeAdmin` act within the user's own home; **`Admin` is the global super-admin** (all homes/users). Policies in `AuthorizationPolicies` + `Program.cs`: `CanRead`, `CanManage`, **`CanAdminHome`** (`HomeAdmin`+`Admin` — own-home member management, gates `/manage/users`), `CanAdmin` (`Admin` only — global, gates `/manage/homes`). Own-home scoping is enforced in the pages/services, not by the policy.
- **Identity**: `ApplicationUser` lives in `HomeDutiesAssistant.Web.Models`; `ApplicationDbContext : IdentityDbContext<ApplicationUser>` uses `EFCore.NamingConventions` (snake_case) **and `HasDefaultSchema("identity")`**. Registered via `AddIdentityCore` (min password length 8, non-alphanumeric not required, `RequireUniqueEmail=false`; usernames are still unique — `CreateAsync` rejects duplicates). There are **no EF migrations and no `DesignTimeDbContextFactory`** — schema comes entirely from `db/init` (see Database schema above).
- **Account confirmation** (stops account/home spam): founder registration is gated, and the **presence of a `Smtp` config section** picks the mechanism — `EmailSender` (+ `SmtpOptions`) is registered **only when `Smtp` exists**, so callers resolve it via `IServiceProvider.GetService<EmailSender>()` (null ⇒ approval mode). **Email mode**: `/register` sends a confirmation link, confirmed at `/confirm`. **Approval mode** (no `Smtp`): an `Admin` clicks **Approve** on `/manage/users`. Both flip Identity's `email_confirmed`; **login is blocked until it's true**. Confirmation gates **only home founding** — members an admin adds are created confirmed, and the seeded `admin` is `email_confirmed = TRUE`. SMTP uses built-in `System.Net.Mail` (SSL always on); no new package.
- **`Auth.Jwt.SigningKey`** must be ≥ 32 bytes; `JwtTokenService` throws otherwise and `Program.cs` hard-fails in Production if missing. Supply via `Auth__Jwt__SigningKey` (compose reads `JWT_SIGNING_KEY` from `.env`).

### Deployment (self-hosted stack)

Repo-root `docker-compose.yml`: **pgvector** (Postgres 17 + `db/init` mount), **web** (multi-stage `Dockerfile`), **caddy** (TLS + reverse proxy, `Caddyfile`, `SITE_ADDRESS`), **seq** (logs). `docker compose up -d --build`. Web reaches Ollama **outside** the stack at `host.docker.internal:11434` (host Ollama must listen on `0.0.0.0`). Secrets/ports from `.env`: `POSTGRES_PASSWORD`, `JWT_SIGNING_KEY`, `SEQ_ADMIN_PASSWORD`, `SITE_ADDRESS`, optional `HTTP_PORT`/`HTTPS_PORT`/`SEQ_UI_PORT`. Caddy serves HTTPS at `https://<SITE_ADDRESS>`; the web container is only `expose`d (not published). The `Dockerfile` does `dotnet publish` in one step (a split restore breaks Blazor's static-web-asset processing).

## Hybrid retrieval (the non-obvious part)

`DutiesRepository.SearchAsync` fuses two rankings with Reciprocal Rank Fusion, **scoped to a home** (`WHERE home_id = $7` in both CTEs and the outer query):

```
score = LexicalWeight/(RrfK + lexicalRank) + VectorWeight/(RrfK + vectorRank)
```

- **lexical**: `pg_trgm` `word_similarity` over category (0.6) + content (0.4).
- **vector**: pgvector cosine distance (`<=>`).

Defaults in `RagOptions`: `LexicalWeight=2.0`, `VectorWeight=1.0`, `RrfK=60`. Lexical is weighted higher **because `nomic-embed-text` is weak on Polish**. Swap in a strong multilingual model → raise `VectorWeight`.

## Important gotchas

- **The `Task` model clashes with `System.Threading.Tasks.Task`.** `Models/Task.cs` is in the globally-imported `HomeDutiesAssistant.Models`, so bare `async Task` is ambiguous wherever Models is in scope. The convention: alias `Task` to the framework type — `@using Task = System.Threading.Tasks.Task` in `Components/_Imports.razor` (covers all components) and `using Task = System.Threading.Tasks.Task;` in Core/web `.cs` files that import Models and use bare `Task` (e.g. `HomeService`, `HomesRepository`, web `Program.cs`). The `Task` **model** itself is referenced via a `TaskModel` alias (`TasksRepository`, `TaskService`, `Tasks.razor`). `Task<T>` is never ambiguous (the model is non-generic).
- **The `Home` model clashes with the `Home.razor` page class** (`HomeDutiesAssistant.Web.Components.Pages.Home`). Inside `Pages/*.razor`, `Home` resolves to the page class — reference the model via a `HomeModel` alias (`Users.razor`) or avoid naming it (capture `long homeId`, as `Register.razor` does).
- **All schema is in `db/init`; the app does no DDL.** Adding/changing a table or column means editing the `db/init` SQL **and** recreating the volume (`down -v`) — there are no migrations and no `InitializeAsync`. Verify SQL edits on a throwaway container first (a syntax error aborts the whole init under `ON_ERROR_STOP`).
- **Adding a field to `Duty`** touches: `ToContext()` (else it's neither embedded nor seen by the model); the `home.duties` table in `db/init/03-home-schema.sql`; `DutiesSql.Insert`/`Update`/`List`; `DutiesRepository.AddDutyParameters` + `ListDutiesAsync`; an input in `Duties.razor`.
- **`nomic-embed-text` requires task prefixes.** Documents use `search_document: `, queries `search_query: ` (`OllamaOptions.EmbedDocumentPrefix`/`EmbedQueryPrefix`). Change the model → update these and `EmbeddingDimensions` (768) to match the `vector(N)` column (hardcoded in `03-home-schema.sql`; changing it means recreating the table).
- **`data/*.yaml` lives in `HomeDutiesAssistant.Core`** (`<Content>`) and flows to both front-ends' output via the project reference. After editing: the console needs `dotnet run -- ingest`; the web re-ingests on its next Quartz run. Records upsert by `(home_id, title)`; ingestion seeds into the default Home only.
- **Two `appsettings.json`** (console + web) — keep `Ollama`/`Database`/`Rag` in sync; the web one adds `Serilog` + `Auth`. Both read `data/` from `AppContext.BaseDirectory`.
- **Membership is a plain insert** (`HomesSql.InsertUserHome`) — `AssignAsync` only works for a user with no home yet. Moving an existing user between homes would need an explicit `UPDATE` (deliberately not implemented; the Users page assigns on create only).