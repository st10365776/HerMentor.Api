using System.Net.Mail;
using HerMentor.Api.Auth;
using HerMentor.Api.Data;
using HerMentor.Api.Hubs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace HerMentor.Api.Endpoints;

public static class AuthEndpoints
{
	// Used to keep response time similar when the email is unknown.
	private static readonly string DummyHash = PasswordHasher.Hash("not-a-real-password");

	public static void MapAuth(this WebApplication app)
	{
		var group = app.MapGroup("/api/auth");

		group.MapPost("/register", Register).RequireRateLimiting("auth");
		group.MapPost("/login", Login).RequireRateLimiting("auth");
		group.MapGet("/me", Me).RequireAuthorization();
	}

	private static async Task<IResult> Register(RegisterRequest req, AppDb db, TokenService tokens, IHubContext<MentorsHub> hub)
	{
		var name = (req.Name ?? "").Trim();
		var email = (req.Email ?? "").Trim().ToLowerInvariant();
		var role = (req.Role ?? "mentee").Trim().ToLowerInvariant();

		if (name.Length is < 2 or > 80)
			return Results.BadRequest(new ApiError("Enter your name (2 to 80 characters)."));
		if (!IsValidEmail(email))
			return Results.BadRequest(new ApiError("Enter a valid email address."));
		if ((req.Password ?? "").Length is < 8 or > 128)
			return Results.BadRequest(new ApiError("Password must be 8 to 128 characters."));
		if (role is not ("mentee" or "mentor"))
			return Results.BadRequest(new ApiError("Role must be mentee or mentor."));

		if (await db.Users.AnyAsync(u => u.Email == email))
			return Results.Conflict(new ApiError("An account with this email already exists."));

		var user = new User { Name = name, Email = email, PasswordHash = PasswordHasher.Hash(req.Password!), Role = role };
		db.Users.Add(user);
		await db.SaveChangesAsync();

		if (role == "mentor")
		{
			var mentor = new Mentor { UserId = user.Id, Name = name, Title = "New mentor", Category = "General" };
			db.Mentors.Add(mentor);
			await db.SaveChangesAsync();
			await hub.NotifyMentorsChanged(mentor.Id, "added");
		}

		return Results.Ok(new AuthResponse(tokens.Create(user), ToDto(user)));
	}

	private static async Task<IResult> Login(LoginRequest req, AppDb db, TokenService tokens)
	{
		var email = (req.Email ?? "").Trim().ToLowerInvariant();
		var user = await db.Users.FirstOrDefaultAsync(u => u.Email == email);

		var valid = PasswordHasher.Verify(req.Password ?? "", user?.PasswordHash ?? DummyHash);
		if (user is null || !valid)
			return Results.Json(new ApiError("Incorrect email or password."), statusCode: StatusCodes.Status401Unauthorized);

		return Results.Ok(new AuthResponse(tokens.Create(user), ToDto(user)));
	}

	private static async Task<IResult> Me(System.Security.Claims.ClaimsPrincipal principal, AppDb db)
	{
		var user = await db.Users.FindAsync(principal.UserId());
		return user is null ? Results.Unauthorized() : Results.Ok(ToDto(user));
	}

	private static UserDto ToDto(User u) => new(u.Id, u.Name, u.Email, u.Role);

	private static bool IsValidEmail(string email) =>
		email.Length <= 254 && MailAddress.TryCreate(email, out var parsed) && parsed.Address == email && email.Contains('.');
}
