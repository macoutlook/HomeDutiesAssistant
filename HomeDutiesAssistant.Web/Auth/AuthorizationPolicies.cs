namespace HomeDutiesAssistant.Web.Auth;

// Hierarchical access policies: Admin ⊇ HomeAdmin ⊇ Manage ⊇ Read. A user carries
// one role claim; each policy accepts that role plus any higher one. CanAdminHome
// grants member management within the user's own home (HomeAdmin+); CanAdmin is
// the global super-admin gate (Admin only). Own-home scoping is enforced in the
// pages/services, not by the policy.
public static class AuthorizationPolicies
{
    public const string CanRead = nameof(CanRead);
    public const string CanManage = nameof(CanManage);
    public const string CanAdminHome = nameof(CanAdminHome);
    public const string CanAdmin = nameof(CanAdmin);
}