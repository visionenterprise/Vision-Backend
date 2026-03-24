using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace vision_backend.Infrastructure.Realtime;

[Authorize]
public class NotificationsHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var userIdClaim = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!string.IsNullOrWhiteSpace(userIdClaim))
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, GroupNameForUser(userIdClaim));
        }

        await base.OnConnectedAsync();
    }

    public static string GroupNameForUser(Guid userId) => $"user:{userId}";

    private static string GroupNameForUser(string userId) => $"user:{userId}";
}
