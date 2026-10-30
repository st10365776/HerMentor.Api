using System.Security.Claims;
using System.Text.Json;
using HerMentor.Api.Auth;
using HerMentor.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace HerMentor.Api.Endpoints;

public record BookRequest(int MentorId, DateTime StartsAt, string? Format, string? Topic, string? Goal);
public record SavePrepRequest(PrepResponse Prep);
public record OutcomeRequest(string? Summary, List<string>? Actions, string? Feedback);
public record SessionActionDto(int Id, string Text, bool Done);
public record SessionDto(
	int Id,
	int MentorId,
	string MentorName,
	string MentorInitials,
	string MenteeName,
	bool IsMentorView,
	DateTime StartsAt,
	string Format,
	string Topic,
	string Goal,
	string Status,
	string Summary,
	string MentorFeedback,
	List<SessionActionDto> Actions,
	PrepResponse? Prep);

public static class SessionEndpoints
{
	private static readonly string[] Formats = ["Video", "Voice", "Chat"];
	private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

	public static void MapSessions(this WebApplication app)
	{
		var group = app.MapGroup("/api/sessions").RequireAuthorization();

		group.MapGet("/", List);
		group.MapGet("/busy", Busy);
		group.MapPost("/", Book);
		group.MapPost("/{id:int}/cancel", Cancel);
		group.MapPut("/{id:int}/prep", SavePrep);
		group.MapPost("/{id:int}/outcome", LogOutcome);
		group.MapPost("/{id:int}/actions/{actionId:int}/toggle", ToggleAction);
	}

	private static async Task<IResult> List(ClaimsPrincipal principal, AppDb db)
	{
		var userId = principal.UserId();
		var myMentorId = await db.Mentors.AsNoTracking().Where(m => m.UserId == userId).Select(m => (int?)m.Id).FirstOrDefaultAsync();

		var sessions = await db.Sessions.AsNoTracking()
			.Include(s => s.Actions)
			.Where(s => s.MenteeId == userId || (myMentorId != null && s.MentorId == myMentorId))
			.OrderBy(s => s.StartsAt)
			.ToListAsync();

		return Results.Ok(await ToDtos(sessions, userId, db));
	}

	/// <summary>Times already taken for a mentor, so the app can grey them out.</summary>
	private static async Task<IResult> Busy(int mentorId, AppDb db)
	{
		var now = DateTime.UtcNow;
		var taken = await db.Sessions.AsNoTracking()
			.Where(s => s.MentorId == mentorId && s.Status != SessionStatus.Cancelled && s.StartsAt > now)
			.Select(s => s.StartsAt)
			.ToListAsync();

		return Results.Ok(taken.Select(t => DateTime.SpecifyKind(t, DateTimeKind.Utc)));
	}

	private static async Task<IResult> Book(BookRequest req, ClaimsPrincipal principal, AppDb db)
	{
		var userId = principal.UserId();

		var mentor = await db.Mentors.AsNoTracking().FirstOrDefaultAsync(m => m.Id == req.MentorId);
		if (mentor is null)
			return Results.NotFound(new ApiError("Mentor not found."));
		if (mentor.UserId == userId)
			return Results.BadRequest(new ApiError("You can't book a session with yourself."));
		if (!mentor.IsAvailable)
			return Results.Json(new ApiError($"{mentor.Name} isn't taking new sessions right now."), statusCode: StatusCodes.Status409Conflict);

		var start = req.StartsAt.Kind == DateTimeKind.Unspecified
			? DateTime.SpecifyKind(req.StartsAt, DateTimeKind.Utc)
			: req.StartsAt.ToUniversalTime();

		if (start < DateTime.UtcNow.AddMinutes(15))
			return Results.BadRequest(new ApiError("Pick a time in the future."));
		if (start > DateTime.UtcNow.AddDays(60))
			return Results.BadRequest(new ApiError("You can book up to 60 days ahead."));
		if (start.Second != 0 || start.Millisecond != 0 || start.Minute % 30 != 0)
			return Results.BadRequest(new ApiError("Sessions start on the hour or half hour."));

		var format = Formats.FirstOrDefault(f => f.Equals(req.Format?.Trim(), StringComparison.OrdinalIgnoreCase)) ?? "Video";
		var goal = ProfileEndpoints.Clip(req.Goal, 300);
		var topic = ProfileEndpoints.Clip(req.Topic, 80);

		var clash = await db.Sessions.AnyAsync(s => s.MenteeId == userId && s.StartsAt == start && s.Status != SessionStatus.Cancelled);
		if (clash)
			return Results.Json(new ApiError("You already have a session at that time."), statusCode: StatusCodes.Status409Conflict);

		var session = new Session
		{
			MenteeId = userId,
			MentorId = mentor.Id,
			StartsAt = start,
			Format = format,
			Topic = topic.Length > 0 ? topic : "Career conversation",
			Goal = goal
		};

		db.Sessions.Add(session);
		try
		{
			await db.SaveChangesAsync();
		}
		catch (DbUpdateException)
		{
			return Results.Json(new ApiError("That time was just taken. Please pick another."), statusCode: StatusCodes.Status409Conflict);
		}

		var dtos = await ToDtos([session], userId, db);
		return Results.Ok(dtos[0]);
	}

	private static async Task<IResult> Cancel(int id, ClaimsPrincipal principal, AppDb db)
	{
		var (session, error) = await Load(id, principal, db);
		if (session is null)
			return error!;
		if (session.Status != SessionStatus.Booked)
			return Results.Json(new ApiError("Only upcoming sessions can be cancelled."), statusCode: StatusCodes.Status409Conflict);

		session.Status = SessionStatus.Cancelled;
		await db.SaveChangesAsync();
		return Results.Ok(await Single(session, principal.UserId(), db));
	}

	private static async Task<IResult> SavePrep(int id, SavePrepRequest req, ClaimsPrincipal principal, AppDb db)
	{
		var (session, error) = await Load(id, principal, db);
		if (session is null)
			return error!;

		var prep = req.Prep;
		if (prep is null || prep.Questions is null || prep.Agenda is null)
			return Results.BadRequest(new ApiError("Nothing to save."));

		var clean = new PrepResponse(
			ProfileEndpoints.Clip(prep.Goal, 300),
			ProfileEndpoints.Clip(prep.MentorName, 80),
			prep.Questions.Select(q => ProfileEndpoints.Clip(q, 160)).Where(q => q.Length > 0).Take(6).ToList(),
			prep.Agenda.Select(a => new AgendaItemDto(ProfileEndpoints.Clip(a.Item, 60), Math.Clamp(a.Minutes, 1, 120))).Where(a => a.Item.Length > 0).Take(6).ToList(),
			prep.AiGenerated);

		session.PrepJson = JsonSerializer.Serialize(clean, Json);
		if (clean.Goal.Length > 0)
			session.Goal = clean.Goal;

		await db.SaveChangesAsync();
		return Results.Ok(await Single(session, principal.UserId(), db));
	}

	/// <summary>The mentee records what happened; the mentor adds feedback. Either one completes the session.</summary>
	private static async Task<IResult> LogOutcome(int id, OutcomeRequest req, ClaimsPrincipal principal, AppDb db)
	{
		var userId = principal.UserId();
		var (session, error) = await Load(id, principal, db);
		if (session is null)
			return error!;
		if (session.Status == SessionStatus.Cancelled)
			return Results.Json(new ApiError("This session was cancelled."), statusCode: StatusCodes.Status409Conflict);

		var isMentee = session.MenteeId == userId;

		if (isMentee)
		{
			session.Summary = ProfileEndpoints.Clip(req.Summary, 600);

			var wanted = ProfileEndpoints.Clean(req.Actions);
			var existing = session.Actions.ToList();
			foreach (var action in existing.Where(a => !wanted.Contains(a.Text, StringComparer.OrdinalIgnoreCase)))
				db.SessionActions.Remove(action);
			foreach (var text in wanted.Where(t => !existing.Any(a => a.Text.Equals(t, StringComparison.OrdinalIgnoreCase))))
				session.Actions.Add(new SessionAction { Text = text });
		}
		else
		{
			session.MentorFeedback = ProfileEndpoints.Clip(req.Feedback, 600);
		}

		session.Status = SessionStatus.Completed;
		await db.SaveChangesAsync();
		return Results.Ok(await Single(session, userId, db));
	}

	private static async Task<IResult> ToggleAction(int id, int actionId, ClaimsPrincipal principal, AppDb db)
	{
		var (session, error) = await Load(id, principal, db);
		if (session is null)
			return error!;

		var action = session.Actions.FirstOrDefault(a => a.Id == actionId);
		if (action is null)
			return Results.NotFound(new ApiError("Action item not found."));

		action.Done = !action.Done;
		await db.SaveChangesAsync();
		return Results.Ok(await Single(session, principal.UserId(), db));
	}

	/// <summary>Loads a session only if the caller is its mentee or its mentor.</summary>
	private static async Task<(Session? Session, IResult? Error)> Load(int id, ClaimsPrincipal principal, AppDb db)
	{
		var userId = principal.UserId();
		var session = await db.Sessions.Include(s => s.Actions).FirstOrDefaultAsync(s => s.Id == id);

		if (session is not null)
		{
			var mentorUserId = await db.Mentors.AsNoTracking().Where(m => m.Id == session.MentorId).Select(m => m.UserId).FirstOrDefaultAsync();
			if (session.MenteeId == userId || mentorUserId == userId)
				return (session, null);
		}

		return (null, Results.NotFound(new ApiError("Session not found.")));
	}

	private static async Task<SessionDto> Single(Session session, int userId, AppDb db) =>
		(await ToDtos([session], userId, db))[0];

	private static async Task<List<SessionDto>> ToDtos(List<Session> sessions, int userId, AppDb db)
	{
		var mentorIds = sessions.Select(s => s.MentorId).Distinct().ToList();
		var menteeIds = sessions.Select(s => s.MenteeId).Distinct().ToList();

		var mentors = await db.Mentors.AsNoTracking().Where(m => mentorIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id);
		var mentees = await db.Users.AsNoTracking().Where(u => menteeIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Name);

		return sessions.Select(s =>
		{
			mentors.TryGetValue(s.MentorId, out var mentor);
			var mentorName = mentor?.Name ?? "Mentor";

			return new SessionDto(
				s.Id,
				s.MentorId,
				mentorName,
				Matching.Initials(mentorName),
				mentees.GetValueOrDefault(s.MenteeId, "Mentee"),
				mentor?.UserId == userId && s.MenteeId != userId,
				DateTime.SpecifyKind(s.StartsAt, DateTimeKind.Utc),
				s.Format,
				s.Topic,
				s.Goal,
				s.Status,
				s.Summary,
				s.MentorFeedback,
				s.Actions.OrderBy(a => a.Id).Select(a => new SessionActionDto(a.Id, a.Text, a.Done)).ToList(),
				ReadPrep(s.PrepJson));
		}).ToList();
	}

	private static PrepResponse? ReadPrep(string? json)
	{
		if (string.IsNullOrWhiteSpace(json))
			return null;

		try
		{
			return JsonSerializer.Deserialize<PrepResponse>(json, Json);
		}
		catch (JsonException)
		{
			return null;
		}
	}
}
