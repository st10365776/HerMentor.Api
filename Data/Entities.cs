namespace HerMentor.Api.Data;

public class User
{
	public int Id { get; set; }
	public required string Name { get; set; }
	public required string Email { get; set; }
	public required string PasswordHash { get; set; }
	public required string Role { get; set; }
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public MenteeProfile? Profile { get; set; }
}

public class MenteeProfile
{
	public int UserId { get; set; }
	public string Education { get; set; } = "";
	public List<string> Skills { get; set; } = [];
	public List<string> Interests { get; set; } = [];
	public string Goal { get; set; } = "";
	public string Availability { get; set; } = "";
	public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class Mentor
{
	public int Id { get; set; }
	public int? UserId { get; set; }
	public required string Name { get; set; }
	public string Title { get; set; } = "";
	public string Category { get; set; } = "General";
	public string Bio { get; set; } = "";
	public List<string> Tags { get; set; } = [];
	public int Years { get; set; }
	public double Rating { get; set; }
	public bool IsAvailable { get; set; } = true;
	public string Availability { get; set; } = "";
	public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public class Session
{
	public int Id { get; set; }
	public int MenteeId { get; set; }
	public int MentorId { get; set; }
	public DateTime StartsAt { get; set; }
	public string Format { get; set; } = "Video";
	public string Topic { get; set; } = "";
	public string Goal { get; set; } = "";
	public string Status { get; set; } = SessionStatus.Booked;
	public string? PrepJson { get; set; }
	public string Summary { get; set; } = "";
	public string MentorFeedback { get; set; } = "";
	public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
	public List<SessionAction> Actions { get; set; } = [];
}

public class SessionAction
{
	public int Id { get; set; }
	public int SessionId { get; set; }
	public string Text { get; set; } = "";
	public bool Done { get; set; }
}

public static class SessionStatus
{
	public const string Booked = "Booked";
	public const string Completed = "Completed";
	public const string Cancelled = "Cancelled";
}
