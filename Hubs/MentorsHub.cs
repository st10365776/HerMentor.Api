using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace HerMentor.Api.Hubs;

/// <summary>
/// Clients connect here and receive "MentorsChanged" whenever a mentor is added or updated.
/// </summary>
[Authorize]
public class MentorsHub : Hub
{
	public const string MentorsChanged = "MentorsChanged";
}

public static class MentorsHubExtensions
{
	public static Task NotifyMentorsChanged(this IHubContext<MentorsHub> hub, int mentorId, string kind) =>
		hub.Clients.All.SendAsync(MentorsHub.MentorsChanged, new { mentorId, kind });
}
