using HerMentor.Api.Data;

namespace HerMentor.Api;

public static class Matching
{
	public static int Score(MenteeProfile? profile, Mentor mentor)
	{
		if (profile is null || profile.Skills.Count + profile.Interests.Count == 0)
			return 50;

		var wanted = profile.Skills.Concat(profile.Interests).Select(Norm).ToHashSet();
		var offered = mentor.Tags.Select(Norm).Append(Norm(mentor.Category)).ToHashSet();

		var score = 50 + Math.Min(wanted.Intersect(offered).Count() * 15, 40);
		if (profile.Interests.Any(i => Norm(i) == Norm(mentor.Category)))
			score += 5;
		if (mentor.IsAvailable)
			score += 3;

		return Math.Min(score, 99);
	}

	public static string Initials(string name)
	{
		var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		return parts.Length switch
		{
			0 => "?",
			1 => parts[0][..1].ToUpperInvariant(),
			_ => $"{parts[0][0]}{parts[^1][0]}".ToUpperInvariant()
		};
	}

	public static MentorDto ToDto(Mentor m, MenteeProfile? profile) => new(
		m.Id, m.Name, Initials(m.Name), m.Title, m.Category, m.Bio, m.Tags, m.Years, m.Rating,
		m.IsAvailable, m.Availability, Score(profile, m));

	private static string Norm(string value) => value.Trim().ToLowerInvariant();
}
