namespace HomeDutiesAssistant.Infrastructure;

internal static class TasksSql
{
    // New tasks go to the end of the home's list (highest priority number).
    public const string Insert = """
        INSERT INTO rag.tasks (home_id, title, due_date, status, priority)
        VALUES ($1, $2, $3, $4, COALESCE((SELECT MAX(priority) FROM rag.tasks WHERE home_id = $1), 0) + 1)
        RETURNING id
        """;

    public const string Update = """
        UPDATE rag.tasks SET title = $3, due_date = $4, status = $5
        WHERE id = $1 AND home_id = $2
        """;

    public const string Delete = """
        DELETE FROM rag.tasks WHERE id = $1 AND home_id = $2
        """;

    // Rewrite priority to each id's 1-based position in the supplied array.
    // unnest() WITH ORDINALITY rewind all records. In case of bigger set of data other approach should be considered.
    public const string Reorder = """
        UPDATE rag.tasks AS t
        SET priority = ordered.ord
        FROM unnest($2::bigint[]) WITH ORDINALITY AS ordered(id, ord)
        WHERE t.id = ordered.id AND t.home_id = $1
        """;

    public const string List = """
        SELECT id, home_id, title, due_date, status, priority
        FROM rag.tasks
        WHERE home_id = $1
        ORDER BY priority, id
        """;

    public const string Count = """
        SELECT COUNT(*) FROM rag.tasks WHERE home_id = $1
        """;
}