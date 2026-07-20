namespace HomeDutiesAssistant.Infrastructure;

internal static class HomesSql
{
    public const string Insert = """
        INSERT INTO home.homes (name) VALUES ($1) RETURNING id
        """;

    public const string SelectByName = """
        SELECT id, name FROM home.homes WHERE name = $1
        """;

    public const string List = """
        SELECT id, name FROM home.homes ORDER BY name
        """;

    public const string InsertUserHome = """
        INSERT INTO home.user_homes (user_id, home_id) VALUES ($1, $2)
        """;

    public const string SelectHomeByUser = """
        SELECT h.id, h.name
        FROM home.user_homes uh
        JOIN home.homes h ON h.id = uh.home_id
        WHERE uh.user_id = $1
        """;

    public const string SelectUserIdsByHome = """
        SELECT user_id FROM home.user_homes WHERE home_id = $1
        """;
    
    public const string Delete = """
        DELETE FROM home.homes WHERE id = $1
        """;
}