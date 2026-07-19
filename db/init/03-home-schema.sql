-- Extensions stay in public so the vector type + pg_trgm functions resolve via
-- the default search_path; the domain tables live in their own `home` schema.
CREATE EXTENSION IF NOT EXISTS vector;
CREATE EXTENSION IF NOT EXISTS pg_trgm;

CREATE SCHEMA IF NOT EXISTS home;

CREATE TABLE IF NOT EXISTS home.homes (
    id   BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    name TEXT NOT NULL UNIQUE
);

-- vector(768) must match Rag:EmbeddingDimensions in appsettings.
CREATE TABLE IF NOT EXISTS home.duties (
    id         BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    home_id    BIGINT NOT NULL REFERENCES home.homes (id) ON DELETE CASCADE,
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

CREATE TABLE IF NOT EXISTS home.tasks (
    id       BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    home_id  BIGINT NOT NULL REFERENCES home.homes (id) ON DELETE CASCADE,
    title    TEXT NOT NULL,
    due_date DATE,
    status   TEXT NOT NULL DEFAULT 'Todo',
    priority INTEGER NOT NULL DEFAULT 0
);

-- Which home each user belongs to. PRIMARY KEY (user_id) => one home per user
-- (no switching); many users per home. Lives in the home schema so identity
-- stays free of any dependency on it.
CREATE TABLE IF NOT EXISTS home.user_homes (
    user_id TEXT   NOT NULL PRIMARY KEY REFERENCES identity.users (id) ON DELETE CASCADE,
    home_id BIGINT NOT NULL REFERENCES home.homes (id) ON DELETE CASCADE
);

CREATE INDEX IF NOT EXISTS ix_user_homes_home_id ON home.user_homes (home_id);