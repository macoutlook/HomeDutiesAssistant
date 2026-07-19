namespace HomeDutiesAssistant.Infrastructure;

internal static class DutiesSql
{
    // CREATE — dedupe on the (home_id, title) key; let the identity column assign
    // the id for genuinely new rows and return it.
    public const string Insert = """
        INSERT INTO home.duties
            (home_id, category, title, provider, amount, currency, due_date,
             frequency, notes, content, embedding)
        VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11)
        ON CONFLICT (home_id, title) DO UPDATE
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

    // EDIT — update the known row by id ($1) within its home ($2); fields follow
    // ($3..$12). A title rename that collides within the home raises a unique
    // violation.
    public const string Update = """
        UPDATE home.duties SET
            category  = $3,  title     = $4,  provider  = $5,
            amount    = $6,  currency  = $7,  due_date  = $8,
            frequency = $9,  notes     = $10, content   = $11,
            embedding = $12
        WHERE id = $1 AND home_id = $2
        """;

    public const string List = """
        SELECT id, home_id, category, title, provider, amount, currency, due_date, frequency, notes
        FROM home.duties
        WHERE home_id = $1
        ORDER BY category, title
        """;

    public const string Delete = """
        DELETE FROM home.duties WHERE id = $1 AND home_id = $2
        """;

    public const string Count = """
        SELECT COUNT(*) FROM home.duties WHERE home_id = $1
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
            FROM home.duties
            WHERE home_id = $7
        ),
        lex AS (
            SELECT id, ROW_NUMBER() OVER (
                ORDER BY (0.6 * word_similarity($2, category)
                        + 0.4 * word_similarity($2, content)) DESC
            ) AS rank
            FROM home.duties
            WHERE home_id = $7
        )
        SELECT d.content, (d.embedding <=> $1) AS distance
        FROM home.duties d
        JOIN vec ON vec.id = d.id
        JOIN lex ON lex.id = d.id
        ORDER BY ($3 / ($5 + lex.rank::float) + $4 / ($5 + vec.rank::float)) DESC
        LIMIT $6
        """;
}