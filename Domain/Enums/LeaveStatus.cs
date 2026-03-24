namespace vision_backend.Domain.Enums;

public enum LeaveStatus
{
    PendingPiller = 0,
    ApprovedPiller = 1,
    RejectedPiller = 2,
    PendingAdmin = 3,
    ApprovedAdmin = 4,
    RejectedAdmin = 5,
    PendingSuperAdmin = 6,
    Approved = 7,
    Rejected = 8,
}
