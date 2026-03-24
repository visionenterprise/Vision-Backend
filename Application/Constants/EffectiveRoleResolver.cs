using vision_backend.Domain.Entities;
using vision_backend.Domain.Enums;

namespace vision_backend.Application.Constants;

public static class EffectiveRoleResolver
{
    public const string PillerRoleName = "piller";

    public static UserRole GetEffectiveRole(User user)
    {
        if (user.Role == UserRole.Admin && IsPillerRoleName(user.AdminRole?.Name))
            return UserRole.Piller;

        return user.Role;
    }

    public static bool IsEffectivePiller(User user)
        => GetEffectiveRole(user) == UserRole.Piller;

    public static bool IsPillerRoleName(string? roleName)
        => !string.IsNullOrWhiteSpace(roleName)
           && roleName.Trim().Equals(PillerRoleName, StringComparison.OrdinalIgnoreCase);
}