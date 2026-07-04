namespace HomeDutiesAssistant.Infrastructure;

// All SQL for the duties knowledge base, kept together as raw string literals so
// the queries live in one place and DutiesVector stays focused on execution.
internal static class DutiesSql
{
    public const string CreateVectorExtension = """
        CREATE EXTENSION IF NOT EXISTS vector
        """;

    public const string CreateTrigramExtension = """
        CREATE EXTENSION IF NOT EXISTS pg_trgm
        """;

    // {0} = embedding dimensions. The structured Duty fields live here too, so
    // the DB is the source of truth and the management UI can read them back to
    // pre-fill an edit. `content` is the canonical sentence (Duty.ToContext) we
    // embed + show the model; it is recomputed whenever a duty is saved.
    public const string CreateTable = """
        CREATE TABLE IF NOT EXISTS duties (
            id         BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
            category   TEXT NOT NULL,
            title      TEXT NOT NULL UNIQUE,
            provider   TEXT,
            amount     NUMERIC,
            currency   TEXT,
            due_date   TEXT,
            frequency  TEXT,
            notes      TEXT,
            content    TEXT NOT NULL,
            embedding  vector({0}) NOT NULL
        )
        """;

    // CREATE — dedupe on the title key; let the identity column assign the id
    // for genuinely new rows and return it.
    public const string Insert = """
        INSERT INTO duties
            (category, title, provider, amount, currency, due_date,
             frequency, notes, content, embedding)
        VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)
        ON CONFLICT (title) DO UPDATE
            SET category  = EXCLUDED.category,
                provider  = EXCLUDED.provider,
                amount    = EXCLUDED.amount,
                currency  = EXCLUDED.currency,
                due_date  = EXCLUDED.due_date,
                frequency = EXCLUDED.frequency,
                notes     = EXCLUDED.notes,
                content   = EXCLUDED.content,
                embedding = EXCLUDED.embedding
        RETURNING id
        """;

    // EDIT — update the known row by id ($1); fields follow ($2..$11). A title
    // rename that collides with another row raises a unique violation.
    public const string Update = """
        UPDATE duties SET
            category  = $2,  title     = $3,  provider  = $4,
            amount    = $5,  currency  = $6,  due_date  = $7,
            frequency = $8,  notes     = $9,  content   = $10,
            embedding = $11
        WHERE id = $1
        """;

    public const string List = """
        SELECT id, category, title, provider, amount, currency, due_date, frequency, notes
        FROM duties
        ORDER BY category, title
        """;

    public const string Delete = """
        DELETE FROM duties WHERE id = $1
        """;

    public const string Count = """
        SELECT COUNT(*) FROM duties
        """;

    // The retrieval step — hybrid search via Reciprocal Rank Fusion (RRF).
    // Each fact is ranked twice: by trigram text match (lexical) and by pgvector
    // cosine distance (semantic). We fuse the two ranks
    //   score = lexW/(k + lexRank) + vecW/(k + vecRank)
    // and return the topK by score. Ranking on position rather than raw scores
    // lets us combine two signals that live on different scales. The lexical
    // score weights the category 0.6 / content 0.4, since the category word is
    // the strongest topic cue. word_similarity(query, target) tolerates the
    // extra words in a natural question (e.g. "ile płacę za prąd").
    public const string Search = """
        WITH vec AS (
            SELECT id, ROW_NUMBER() OVER (ORDER BY embedding <=> $1) AS rank
            FROM duties
        ),
        lex AS (
            SELECT id, ROW_NUMBER() OVER (
                ORDER BY (0.6 * word_similarity($2, category)
                        + 0.4 * word_similarity($2, content)) DESC
            ) AS rank
            FROM duties
        )
        SELECT d.content, (d.embedding <=> $1) AS distance
        FROM duties d
        JOIN vec ON vec.id = d.id
        JOIN lex ON lex.id = d.id
        ORDER BY ($3 / ($5 + lex.rank::float) + $4 / ($5 + vec.rank::float)) DESC
        LIMIT $6
        """;
}