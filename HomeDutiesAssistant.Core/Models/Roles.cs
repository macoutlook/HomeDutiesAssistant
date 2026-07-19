namespace HomeDutiesAssistant.Models;

// The access roles, as plain string constants so they can be used both as claim
// values and as the `role` column. Hierarchical: Admin ⊇ HomeAdmin ⊇ Manage ⊇
// Read. A user carries exactly ONE role, and a policy accepts that role plus any
// higher one. Read/Manage/HomeAdmin act within the user's own home; Admin is the
// global super-admin (all homes and users).
public static class Roles
{
    public const string Read = "Read";           // chat (own home)
    public const string Manage = "Manage";       // chat + manage duties/tasks (own home)
    public const string HomeAdmin = "HomeAdmin"; // + manage members of own home
    public const string Admin = "Admin";         // everything, all homes (super-admin)

    public static readonly IReadOnlyList<string> All = [Read, Manage, HomeAdmin, Admin];

    public static bool IsValid(string? role) => role is Read or Manage or HomeAdmin or Admin;
}