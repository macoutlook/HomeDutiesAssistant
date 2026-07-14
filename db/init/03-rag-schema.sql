-- Extensions stay in public so the vector type + pg_trgm functions resolve via
-- the default search_path; the domain tables live in their own `rag` schema.
CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS pg_trgm;

CREATE SCHEMA IF NOT EXISTS rag;

CREATE TABLE IF NOT EXISTS rag.homes (
    id   BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name TEXT NOT NULL UNIQUE
);

-- vector(768) must match Rag:EmbeddingDimensions in appsettings.
CREATE TABLE IF NOT EXISTS rag.duties (
    id         BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    home_id    BIGINT NOT NULL REFERENCES rag.homes (id) ON DELETE CASCADE,
    category   TEXT NOT NULL,
    title      TEXT NOT NULL,
    provider   TEXT,
    amount     NUMERIC,
    currency   TEXT,
    due_date   TEXT,
    frequency  TEXT,
    notes      TEXT,
    content    TEXT NOT NULL,
    embedding  vector(768) NOT NULL,
    UNIQUE (home_id, title)
);

CREATE TABLE IF NOT EXISTS rag.tasks (
    id       BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    home_id  BIGINT NOT NULL REFERENCES rag.homes (id) ON DELETE CASCADE,
    title    TEXT NOT NULL,
    due_date DATE,
    status   TEXT NOT NULL DEFAULT 'Todo',
    priority INTEGER NOT NULL DEFAULT 0
);