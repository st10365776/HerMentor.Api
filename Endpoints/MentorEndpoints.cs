using System.Security.Claims;
using HerMentor.Api.Auth;
using HerMentor.Api.Data;
using HerMentor.Api.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace HerMentor.Api.Endpoints;

public static class MentorEndpoints
{
	public static void MapMentors(this WebApplication app)
	{
		var group = app.MapGroup("/api/mentors").RequireAuthorization();

		group.MapGet("/", List);
		group.MapGet("/{id:int}", Get);
		group.MapPut("/me", UpdateMe);
	}

	private static async Task<IResult> List(string? query, string? category, ClaimsPrincipal principal, AppDb db)
	{
		var userId = principal.UserId();
		var profile = await db.MenteeProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId);
		var mentors = await db.Mentors.AsNoTracking().Where(m => m.UserId != userId).ToListAsync();

		var text = (query ?? "").Trim();
		var cat = (category ?? "").Trim();

		var result = mentors
			.Where(m => cat.Length == 0 || cat.Equals("All", StringComparison.OrdinalIgnoreCase) || m.Category.Equals(cat, StringComparison.OrdinalIgnoreCase))
			.Where(m => text.Length == 0
				|| m.Name.Contains(text, StringComparison.OrdinalIgnoreCase)
				|| m.Title.Contains(text, StringComparison.OrdinalIgnoreCase)
				|| m.Category.Contains(text, StringComparison.OrdinalIgnoreCase)
				|| m.Tags.Any(t => t.Contains(text, StringComparison.OrdinalIgnoreCase)))
			.Select(m => Matching.ToDto(m, profile))
			.OrderByDescending(m => m.Match)
			.ThenByDescending(m => m.IsAvailable)
			.ThenByDescending(m => m.Rating)
			.ThenBy(m => m.Name)
			.ToList();

		return Results.Ok(result);
	}

	private static async Task<IResult> Get(int id, ClaimsPrincipal principal, AppDb db)
	{
		var mentor = await db.Mentors.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id);
		if (mentor is null)
			return Results.NotFound(new ApiError("Mentor not found."));

		var profile = await db.MenteeProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == principal.UserId());
		return Results.Ok(Matching.ToDto(mentor, profile));
	}

	private static async Task<IResult> UpdateMe(UpdateMentorRequest req, ClaimsPrincipal principal, AppDb db, IHubContext<MentorsHub> hub)
	{
		if (!principal.IsMentor())
			return Results.Json(new ApiError("Only mentors can edit a mentor profile."), statusCode: StatusCodes.Status403Forbidden);

		var userId = principal.UserId();
		var mentor = await db.Mentors.FirstOrDefaultAsync(m => m.UserId == userId);
		if (mentor is null)
			return Results.NotFound(new ApiError("Mentor profile not found."));

		var years = Math.Clamp(req.Years, 0, 60);
		mentor.Title = ProfileEndpoints.Clip(req.Title, 80);
		mentor.Category = ProfileEndpoints.Clip(req.Category, 40) is { Length: > 0 } c ? c : "General";
		mentor.Bio = ProfileEndpoints.Clip(req.Bio, 600);
		mentor.Tags = ProfileEndpoints.Clean(req.Tags);
		mentor.Years = years;
		mentor.IsAvailable = req.IsAvailable;
		mentor.Availability = ProfileEndpoints.Clip(req.Availability, 120);
		mentor.UpdatedAt = DateTime.UtcNow;

		await db.SaveChangesAsync();
		await hub.NotifyMentorsChanged(mentor.Id, "updated");

		return Results.Ok(Matching.ToDto(mentor, null));
	}
}
