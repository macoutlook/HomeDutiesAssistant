namespace HomeDutiesAssistant.Infrastructure;

internal static class HomesSql
{
    public const string Insert = """
        INSERT INTO rag.homes (name) VALUES ($1) RETURNING id
        """;

    public const string SelectByName = """
        SELECT id, name FROM rag.homes WHERE name = $1
        """;

    public const string List = """
        SELECT id, name FROM rag.homes ORDER BY name
        """;
}