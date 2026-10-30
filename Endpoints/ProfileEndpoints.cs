using System.Security.Claims;
using HerMentor.Api.Auth;
using HerMentor.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace HerMentor.Api.Endpoints;

public static class ProfileEndpoints
{
	private const int MaxItems = 20;
	private const int MaxItemLength = 40;

	public static void MapProfile(this WebApplication app)
	{
		var group = app.MapGroup("/api/profile").RequireAuthorization();

		group.MapGet("/", async (ClaimsPrincipal principal, AppDb db) =>
		{
			var profile = await db.MenteeProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == principal.UserId());
			return Results.Ok(profile is null ? new MenteeProfileDto("", [], [], "", "") : ToDto(profile));
		});

		group.MapPut("/", async (MenteeProfileDto req, ClaimsPrincipal principal, AppDb db) =>
		{
			var userId = principal.UserId();
			var profile = await db.MenteeProfiles.FirstOrDefaultAsync(p => p.UserId == userId);
			if (profile is null)
			{
				profile = new MenteeProfile { UserId = userId };
				db.MenteeProfiles.Add(profile);
			}

			profile.Education = Clip(req.Education, 120);
			profile.Skills = Clean(req.Skills);
			profile.Interests = Clean(req.Interests);
			profile.Goal = Clip(req.Goal, 200);
			profile.Availability = Clip(req.Availability, 120);
			profile.UpdatedAt = DateTime.UtcNow;

			await db.SaveChangesAsync();
			return Results.Ok(ToDto(profile));
		});
	}

	private static MenteeProfileDto ToDto(MenteeProfile p) => new(p.Education, p.Skills, p.Interests, p.Goal, p.Availability);

	internal static string Clip(string? value, int max)
	{
		var text = (value ?? "").Trim();
		return text.Length <= max ? text : text[..max];
	}

	internal static List<string> Clean(IEnumerable<string>? items) =>
		(items ?? [])
			.Select(i => Clip(i, MaxItemLength))
			.Where(i => i.Length > 0)
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Take(MaxItems)
			.ToList();
}
