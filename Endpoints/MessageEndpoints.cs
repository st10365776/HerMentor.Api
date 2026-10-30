using System.Security.Claims;
using HerMentor.Api.Auth;
using HerMentor.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace HerMentor.Api.Endpoints;

public record SendMessageRequest(string? Content);
public record MessageDto(int Id, int SessionId, int SenderId, string SenderName, string Content, DateTime CreatedAt, bool IsMine);
public record MessageDeletedDto(int Id);
public record ChatClearedDto(int DeletedCount);

public static class MessageEndpoints
{
	private const int MaxContentLength = 2000;
	private const int MaxMessages = 500;

	public static void MapMessages(this WebApplication app)
	{
		var group = app.MapGroup("/api/sessions/{sessionId:int}/messages").RequireAuthorization();

		group.MapGet("/", List);
		group.MapPost("/", Send);
		group.MapDelete("/{messageId:int}", Delete);
		group.MapDelete("/", Clear);
	}

	private static async Task<IResult> List(int sessionId, ClaimsPrincipal principal, AppDb db)
	{
		var userId = principal.UserId();
		if (!await IsParticipant(sessionId, userId, db))
			return NotFound();

		var messages = await db.Messages.AsNoTracking()
			.Where(m => m.SessionId == sessionId)
			.Include(m => m.Sender)
			.OrderByDescending(m => m.Id)
			.Take(MaxMessages)
			.ToListAsync();

		messages.Reverse();
		return Results.Ok(messages.Select(m => ToDto(m, userId)).ToList());
	}

	private static async Task<IResult> Send(int sessionId, SendMessageRequest request, ClaimsPrincipal principal, AppDb db)
	{
		var userId = principal.UserId();
		if (!await IsParticipant(sessionId, userId, db))
			return NotFound();

		var content = (request.Content ?? "").Trim();
		if (content.Length == 0)
			return Results.BadRequest(new ApiError("Enter a message."));
		if (content.Length > MaxContentLength)
			return Results.BadRequest(new ApiError($"Messages can be up to {MaxContentLength} characters."));

		var message = new Message
		{
			SessionId = sessionId,
			SenderId = userId,
			Content = content,
			CreatedAt = DateTime.UtcNow
		};

		db.Messages.Add(message);
		await db.SaveChangesAsync();
		await db.Entry(message).Reference(m => m.Sender).LoadAsync();

		return Results.Ok(ToDto(message, userId));
	}

	private static async Task<IResult> Delete(int sessionId, int messageId, ClaimsPrincipal principal, AppDb db)
	{
		var userId = principal.UserId();
		if (!await IsParticipant(sessionId, userId, db))
			return NotFound();

		var deleted = await db.Messages
			.Where(m => m.SessionId == sessionId && m.Id == messageId)
			.ExecuteDeleteAsync();

		return deleted == 0
			? NotFound()
			: Results.Ok(new MessageDeletedDto(messageId));
	}

	private static async Task<IResult> Clear(int sessionId, ClaimsPrincipal principal, AppDb db)
	{
		var userId = principal.UserId();
		if (!await IsParticipant(sessionId, userId, db))
			return NotFound();

		var deleted = await db.Messages
			.Where(m => m.SessionId == sessionId)
			.ExecuteDeleteAsync();

		return Results.Ok(new ChatClearedDto(deleted));
	}

	private static async Task<bool> IsParticipant(int sessionId, int userId, AppDb db)
	{
		var session = await db.Sessions.AsNoTracking()
			.Where(s => s.Id == sessionId)
			.Select(s => new { s.Id, s.MenteeId, s.MentorId })
			.FirstOrDefaultAsync();

		if (session is null)
			return false;
		if (session.MenteeId == userId)
			return true;

		return await db.Mentors.AsNoTracking()
			.AnyAsync(m => m.Id == session.MentorId && m.UserId == userId);
	}

	private static IResult NotFound() =>
		Results.NotFound(new ApiError("Conversation not found."));

	private static MessageDto ToDto(Message message, int userId) =>
		new(
			message.Id,
			message.SessionId,
			message.SenderId,
			message.Sender?.Name ?? "User",
			message.Content,
			DateTime.SpecifyKind(message.CreatedAt, DateTimeKind.Utc),
			message.SenderId == userId);
}
