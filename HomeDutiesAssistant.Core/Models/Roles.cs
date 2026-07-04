namespace HomeDutiesAssistant.Models;

// The three access roles, as plain string constants so they can be used both as
// claim values and as the `role` column. Hierarchical: Admin ⊇ Manage ⊇ Read.
// Authorization policies (in the web front-end) encode the hierarchy — a user
// carries exactly ONE role, and a policy accepts that role plus any higher one.
public static class Roles
{
    public const string Read = "Read";     // can use the chat
    public const string Manage = "Manage"; // chat + manage duties
    public const string Admin = "Admin";   // everything, incl. user management

    public static readonly IReadOnlyList<string> All = [Read, Manage, Admin];

    public static bool IsValid(string? role) => role is Read or Manage or Admin;
}