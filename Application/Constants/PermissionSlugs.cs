namespace vision_backend.Application.Constants;

public static class PermissionSlugs
{
    public const string DashboardGeneral = "dashboard_general";
    public const string DashboardAnalytical = "dashboard_analytical";
    public const string LeaveApprovals = "leave_approvals";
    public const string LeaveManagement = "leave_management";
    public const string UpcomingLeaves = "upcoming_leaves";
    public const string VoucherManagement = "voucher_management";
    public const string VoucherCategories = "voucher_categories";
    public const string UserManagement = "user_management";
    public const string SiteManagement = "site_management";
    public const string DesignationManagement = "designation_management";
    public const string RoleManagement = "role_management";

    public static readonly HashSet<string> All = new(StringComparer.Ordinal)
    {
        DashboardGeneral,
        DashboardAnalytical,
        LeaveApprovals,
        LeaveManagement,
        UpcomingLeaves,
        VoucherManagement,
        VoucherCategories,
        UserManagement,
        SiteManagement,
        DesignationManagement,
        RoleManagement,
    };

    public static readonly HashSet<string> BaselineForAllEmployees = new(StringComparer.Ordinal)
    {
        DashboardGeneral,
    };

    public static readonly HashSet<string> ManageableInRoleManagement = new(StringComparer.Ordinal)
    {
        DashboardAnalytical,
        LeaveApprovals,
        LeaveManagement,
        UpcomingLeaves,
        VoucherManagement,
        VoucherCategories,
        UserManagement,
        SiteManagement,
        DesignationManagement,
        RoleManagement,
    };
}
