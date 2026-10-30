using System.Security.Claims;
using System.Text.Json;
using HerMentor.Api.Ai;
using HerMentor.Api.Auth;
using HerMentor.Api.Data;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace HerMentor.Api.Endpoints;

public record PrepRequest(int? MentorId, string? Goal);
public record AgendaItemDto(string Item, int Minutes);
public record PrepResponse(string Goal, string MentorName, List<string> Questions, List<AgendaItemDto> Agenda, bool AiGenerated);

public static class PrepEndpoints
{
	private const int MaxGoalLength = 300;

	public static void MapPrep(this WebApplication app)
	{
		app.MapPost("/api/prep", Generate).RequireAuthorization();
	}

	private static async Task<IResult> Generate(
		PrepRequest req,
		ClaimsPrincipal principal,
		AppDb db,
		AiChat ai,
		AskLimiter limiter,
		ILogger<PrepRequest> log,
		CancellationToken ct)
	{
		var userId = principal.UserId();
		var goal = (req.Goal ?? "").Trim();
		if (goal.Length > MaxGoalLength)
			return Results.BadRequest(new ApiError($"Please keep your goal under {MaxGoalLength} characters."));

		if (!limiter.TryAcquire(userId))
			return Results.Json(new ApiError("You're going too fast. Please wait a moment and try again."), statusCode: StatusCodes.Status429TooManyRequests);

		var profile = await db.MenteeProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == userId, ct);
		var mentor = req.MentorId is { } id && id > 0
			? await db.Mentors.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, ct)
			: null;

		if (goal.Length == 0)
			goal = profile is { Goal.Length: > 0 } ? profile.Goal : "Get advice on my next career steps.";

		if (ai.IsConfigured)
		{
			try
			{
				var json = await ai.CompleteAsync(
				[
					new ChatMessage("system", Prompts.PrepSystem + "\n\n" + Prompts.DescribeMentee(profile)),
					new ChatMessage("user", Describe(mentor, goal))
				], ct);

				if (TryParse(json, out var questions, out var agenda))
					return Results.Ok(new PrepResponse(goal, mentor?.Name ?? "", questions, agenda, true));

				log.LogWarning("Session prep: AI reply wasn't usable JSON: {Reply}", json.Length > 400 ? json[..400] : json);
			}
			catch (HubException ex)
			{
				log.LogWarning("Session prep: AI call failed: {Message}", ex.Message);
			}
		}

		return Results.Ok(Fallback(goal, mentor, profile));
	}

	private static string Describe(Mentor? mentor, string goal)
	{
		var about = mentor is null
			? "The mentor has not been chosen yet."
			: $"Mentor: {mentor.Name}, {mentor.Title}. Area: {mentor.Category}. Expertise: {string.Join(", ", mentor.Tags)}. Bio: {mentor.Bio}";
		return $"{about}\nSession goal (written by the user): {goal}";
	}

	private static bool TryParse(string text, out List<string> questions, out List<AgendaItemDto> agenda)
	{
		questions = [];
		agenda = [];

		// Models sometimes wrap JSON in prose or code fences.
		var start = text.IndexOf('{');
		var end = text.LastIndexOf('}');
		if (start < 0 || end <= start)
			return false;

		try
		{
			using var doc = JsonDocument.Parse(text[start..(end + 1)]);
			var root = doc.RootElement;

			questions = root.GetProperty("questions").EnumerateArray()
				.Select(q => q.GetString()?.Trim() ?? "")
				.Where(q => q.Length > 0)
				.Select(q => ProfileEndpoints.Clip(q, 160))
				.Take(5)
				.ToList();

			agenda = root.TryGetProperty("agenda", out var agendaEl) && agendaEl.ValueKind == JsonValueKind.Array
				? agendaEl.EnumerateArray()
					.Select(a => new AgendaItemDto(
						ProfileEndpoints.Clip(a.GetProperty("item").GetString() ?? "", 60),
						Math.Clamp(a.GetProperty("minutes").GetInt32(), 1, 120)))
					.Where(a => a.Item.Length > 0)
					.Take(5)
					.ToList()
				: [];

			if (agenda.Count < 2)
				agenda = DefaultAgenda();

			return questions.Count >= 2;
		}
		catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
		{
			return false;
		}
	}

	private static PrepResponse Fallback(string goal, Mentor? mentor, MenteeProfile? profile)
	{
		var topic = mentor?.Tags.FirstOrDefault() ?? profile?.Interests.FirstOrDefault() ?? "my field";
		var who = mentor is null ? "you" : mentor.Name.Split(' ')[0];

		List<string> questions =
		[
			$"How did you get started in {topic}?",
			"Which skills should I prioritise in the next 3 months?",
			"What would make my work stand out to employers?",
			$"What do you wish you had known earlier, {who}?"
		];

		List<AgendaItemDto> agenda = DefaultAgenda();
		return new PrepResponse(goal, mentor?.Name ?? "", questions, agenda, false);
	}

	private static List<AgendaItemDto> DefaultAgenda() =>
		[new("Intro and goals", 5), new("Deep dive on my goal", 15), new("Next steps", 10)];
}
