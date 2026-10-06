namespace HomeDutiesAssistant.Infrastructure;

internal static class PushSql
{
    // Re-subscribing the same browser updates its keys; a different user on the
    // same browser takes the row over.
    public const string Upsert = """
        INSERT INTO home.push_subscriptions (user_id, home_id, endpoint, encryption_key, auth)
        VALUES ($1, $2, $3, $4, $5)
        ON CONFLICT (endpoint) DO UPDATE
            SET user_id        = EXCLUDED.user_id,
                home_id        = EXCLUDED.home_id,
                encryption_key = EXCLUDED.encryption_key,
                auth           = EXCLUDED.auth
        """;

    public const string Delete = """
        DELETE FROM home.push_subscriptions WHERE endpoint = $1 AND user_id = $2
        """;

    // Used when the push service reports the subscription gone (404/410).
    public const string DeleteByEndpoint = """
        DELETE FROM home.push_subscriptions WHERE endpoint = $1
        """;

    public const string CountByUser = """
        SELECT COUNT(*) FROM home.push_subscriptions WHERE user_id = $1 AND endpoint <> $2
        """;

    public const string ListByHome = """
        SELECT user_id, home_id, endpoint, encryption_key, auth
        FROM home.push_subscriptions
        WHERE home_id = $1
        """;

    // Duties due on either reminder day ($1 = today, $2 = today + lead), across
    // all homes. Tasks are deliberately not reminded (duties only, for now).
    public const string ListDue = """
        SELECT home_id, id, title, due_date
        FROM home.duties
        WHERE due_date IN ($1, $2)
        ORDER BY home_id, due_date, id
        """;
}
