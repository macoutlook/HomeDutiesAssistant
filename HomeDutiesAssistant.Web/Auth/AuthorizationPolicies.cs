namespace HomeDutiesAssistant.Web.Auth;

// Hierarchical access policies: Admin ⊇ Manage ⊇ Read. A user carries one role
// claim; each policy accepts that role plus any higher one.
public static class AuthorizationPolicies
{
    public const string CanRead = nameof(CanRead);
    public const string CanManage = nameof(CanManage);
    public const string CanAdmin = nameof(CanAdmin);
}