namespace HerMentor.Api;

public record ApiError(string Error);

public record RegisterRequest(string Name, string Email, string Password, string Role);
public record LoginRequest(string Email, string Password);
public record UserDto(int Id, string Name, string Email, string Role);
public record AuthResponse(string Token, UserDto User);

public record MenteeProfileDto(string Education, List<string> Skills, List<string> Interests, string Goal, string Availability);

public record MentorDto(
	int Id,
	string Name,
	string Initials,
	string Title,
	string Category,
	string Bio,
	List<string> Tags,
	int Years,
	double Rating,
	bool IsAvailable,
	string Availability,
	int Match);

public record UpdateMentorRequest(
	string Title,
	string Category,
	string Bio,
	List<string> Tags,
	int Years,
	bool IsAvailable,
	string Availability);
