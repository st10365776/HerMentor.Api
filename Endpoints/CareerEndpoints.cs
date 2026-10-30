using System.Security.Claims;
using HerMentor.Api.Auth;
using HerMentor.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace HerMentor.Api.Endpoints;

public static class CareerEndpoints
{
	public static void MapCareer(this WebApplication app)
	{
		var group = app.MapGroup("/api/career").RequireAuthorization();

		group.MapGet("/paths", async (ClaimsPrincipal principal, AppDb db) =>
		{
			var profile = await db.MenteeProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == principal.UserId());
			return Results.Ok(Career.CareerCatalog.Evaluate(profile));
		});

		group.MapGet("/gap", async (string? role, ClaimsPrincipal principal, AppDb db) =>
		{
			var profile = await db.MenteeProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == principal.UserId());
			return Results.Ok(Career.CareerCatalog.Gap(profile, role));
		});
	}
}
