# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A local, offline RAG (retrieval-augmented generation) assistant for household duties (bills, due dates, sewer emptying, etc.). Facts live in YAML files, are embedded and stored in PostgreSQL + pgvector, and answered by a local Ollama LLM. .NET 10. Notably, the knowledge-base data is in Polish, which drives several retrieval design choices (see Hybrid retrieval below).

## Solution layout (3 projects)

The RAG core is transport-agnostic by design, so it lives in a class library that both front-ends reference:

- **`HomeDutiesAssistant.Core`** — the library: `Models`, `Configuration`, `Infrastructure/{DutiesVector, DutiesSql}`, and `Services/{OllamaClient, DataLoader, IngestionService, RagChatService, DutyService}`. The assembly is `.Core` but the namespaces remain `HomeDutiesAssistant.*`, so consumers reference the types unchanged.
- **`HomeDutiesAssistant`** — the console front-end: `App` (hosted service), `ConsoleChat`, `Program`. Owns the YAML in `data/` and `docker-compose.yml`.
- **`HomeDutiesAssistant.Web`** — a Blazor Server front-end (chat UI, a `/manage` CRUD page, and a scheduled ingestion job).

Both front-ends repeat the same DI registrations (options, named `HttpClient`, and the core services) in their own `Program.cs` — there is no shared composition root. If you add/rename a core service, update **both** `Program.cs` files. The one exception today is `DutyService`, which backs the web-only management UI and so is registered **only** in the web `Program.cs`.

## Prerequisites & running

The app depends on two external services that must be up before it runs:

1. **PostgreSQL + pgvector** — `docker compose up -d` (from repo root; uses `HomeDutiesAssistant/docker-compose.yml` via the solution, or run from that dir). Exposes `localhost:5432`, db `homeduties`, user/pass `home`/`home`. Data persists in the `pgdata` volume across restarts.
2. **Ollama** — must be running at `http://localhost:11434` with both models pulled: `ollama pull llama3.1:8b` and `ollama pull nomic-embed-text`.

Commands:

```bash
dotnet build HomeDutiesAssistant.sln          # builds all three projects

# Console front-end (from HomeDutiesAssistant/ or pass --project)
dotnet run                 # chat mode (default); auto-ingests if the DB is empty
dotnet run -- ingest       # (re)embed all /data YAML and upsert, then exit
dotnet run -- chat         # explicit chat mode

# Web front-end (from HomeDutiesAssistant.Web/)
dotnet run                 # serves http://localhost:5080 (see Properties/launchSettings.json)
```

There are **no tests** and no linter configured in this repo.

For the **console**, the first CLI arg selects the command (`App.RunAsync`): `ingest` or `chat`/none. Type `exit` to leave chat. Stdin can be piped (non-interactive fallback in `ConsoleChat.ReadQuestion`).

The **web app** ingestion is not manual — a Quartz job rebuilds the knowledge base on boot and every 6 hours (see Web front-end below).

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

Blazor Server. `Components/Pages/Home.razor` (`@rendermode InteractiveServer`) is the chat: it `await foreach`s `RagAnswer.Tokens`, appending each token to a mutable turn and calling `StateHasChanged` so the answer streams into the browser over the SignalR circuit; it also renders the retrieved `Sources`. `Components/Pages/Duties.razor` (`/manage`) is a CRUD UI over `DutyService` — list/create/edit/delete duties; saving re-embeds via `DutyService.SaveAsync`, so the new fact is immediately searchable without waiting for the next Quartz ingest.

Ingestion is **scheduled, not manual**: `Jobs/IngestionJob.cs` (Quartz, registered in `Program.cs`) fires once at boot (`StartNow`) and every 6 hours. The job owns the idempotent `DutiesVector.InitializeAsync` call, so there is no separate startup schema step. `[DisallowConcurrentExecution]` prevents overlapping runs; `IngestionService` is registered **Scoped** to match Quartz's per-execution DI scope (the console registers it Transient).

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
- **`data/*.yaml` lives only in the console project** and is the single source of truth. The web project **links** those files (`<None Include="..\HomeDutiesAssistant\data\**\*.yaml" Link="data\...">`) rather than holding its own copy. After editing the YAML: the console needs an explicit `dotnet run -- ingest`; the web app re-ingests automatically on its next Quartz run (or restart). Records upsert by `title` (see "The `duties` key model" above); renaming a `title` in YAML inserts a new row and leaves the old one behind (ingestion has no orphan cleanup — only the `/manage` UI deletes).
- Each project's `appsettings.json` and the (copied/linked) `data/**/*.yaml` land in its build output (`CopyToOutputDirectory`); the app reads `data/` from `AppContext.BaseDirectory`, not the source tree. There are **two** `appsettings.json` files (console + web) — keep the `Ollama`/`Database`/`Rag` sections in sync.
- DI registrations are duplicated across the two `Program.cs` files (no shared composition root) — a new/renamed core service must be registered in both.
- Adding a field to `Duty` touches several places, because the structured fields are persisted as their own columns (the DB is the source of truth the `/manage` UI reads back): append it in `ToContext()` (or it won't be embedded or seen by the model); add the column to `DutiesSql.CreateTable`, `Insert`, `Update`, and `List`; bind it in `DutiesVector.AddDutyParameters` and read it in `ListDutiesAsync`; and add an input to `Duties.razor`. Adding a column requires recreating (or migrating) the `duties` table — there are no migrations.