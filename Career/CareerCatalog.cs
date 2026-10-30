using HerMentor.Api.Data;

namespace HerMentor.Api.Career;

public record RoleDef(string Id, string Name, string[] Skills, string[] Interests);

public record CareerPathDto(string Id, string Name, int Fit, List<string> Have, List<string> Missing);
public record CareerPathsResponse(bool NeedsProfile, List<CareerPathDto> Paths);

public record GapSkillDto(string Name, bool Have, string Step);
public record RoleOptionDto(string Id, string Name, int Fit);
public record SkillGapResponse(
	bool NeedsProfile,
	string RoleId,
	string RoleName,
	int Coverage,
	List<GapSkillDto> Skills,
	List<string> NextSteps,
	List<RoleOptionDto> Roles,
	string SuggestedCategory);

public static class CareerCatalog
{
	/// <summary>One concrete next step per skill, shown when the skill is missing.</summary>
	public static readonly Dictionary<string, string> Steps = new(StringComparer.OrdinalIgnoreCase)
	{
		["React"] = "Build a small React app with components, state and routing.",
		["Node.js"] = "Build a REST API with Node.js and connect it to a database.",
		["SQL"] = "Practise SQL joins and aggregates on a sample database.",
		["Cloud"] = "Take a cloud fundamentals course and deploy a project.",
		["DevOps"] = "Set up a CI/CD pipeline and containerise one of your projects.",
		["Python"] = "Complete a Python course and automate a small task with a script.",
		["Figma"] = "Redesign one screen of an app you like in Figma.",
		["Research"] = "Run a short user interview and write up what you learned."
	};

	/// <summary>Maps a role to the mentor category that can best help with it.</summary>
	public static string CategoryFor(RoleDef role) => role.Id switch
	{
		"ux" => "Design",
		"data-analyst" or "ml" => "Data",
		_ => "Tech"
	};

	public static RoleDef? Find(string? id) =>
		Roles.FirstOrDefault(r => r.Id.Equals(id?.Trim(), StringComparison.OrdinalIgnoreCase));

	public static SkillGapResponse Gap(MenteeProfile? profile, string? roleId)
	{
		var paths = Evaluate(profile);
		var role = Find(roleId) ?? Find(paths.Paths.First().Id)!;
		var path = paths.Paths.First(p => p.Id == role.Id);

		var skills = role.Skills
			.Select(s => new GapSkillDto(s, path.Have.Contains(s), Steps.GetValueOrDefault(s, $"Learn the basics of {s}.")))
			.OrderBy(s => s.Have)
			.ToList();

		var coverage = (int)Math.Round(100.0 * skills.Count(s => s.Have) / skills.Count);
		var steps = skills.Where(s => !s.Have).Select(s => s.Step).ToList();
		steps.Add("Ask a mentor to review your work and give feedback.");

		return new SkillGapResponse(
			paths.NeedsProfile,
			role.Id,
			role.Name,
			coverage,
			skills,
			steps,
			paths.Paths.Select(p => new RoleOptionDto(p.Id, p.Name, p.Fit)).ToList(),
			CategoryFor(role));
	}

	public static readonly RoleDef[] Roles =
	[
		new("full-stack", "Full-Stack Developer", ["React", "Node.js", "SQL", "Cloud"], ["Full-stack"]),
		new("ux", "UX Designer", ["Figma", "Research"], ["UX design"]),
		new("cloud", "Cloud Engineer", ["Cloud", "DevOps", "Python"], ["Cloud"]),
		new("data-analyst", "Data Analyst", ["SQL", "Python"], ["AI / Data"]),
		new("ml", "Machine Learning Engineer", ["Python", "Cloud", "SQL"], ["AI / Data"]),
		new("eng-leader", "Engineering Leader", ["Node.js", "Cloud", "DevOps"], ["Leadership", "Full-stack"]),
		new("product", "Product Manager", ["Research", "SQL", "Figma"], ["Product", "Leadership", "Career switch"])
	];

	/// <summary>
	/// Fit = 80% share of the role's skills you already have + 20% if one of your interests matches the role.
	/// </summary>
	public static CareerPathsResponse Evaluate(MenteeProfile? profile)
	{
		var skills = Set(profile?.Skills);
		var interests = Set(profile?.Interests);
		var needsProfile = skills.Count + interests.Count == 0;

		var paths = Roles
			.Select(role =>
			{
				var have = role.Skills.Where(s => skills.Contains(Norm(s))).ToList();
				var missing = role.Skills.Except(have).ToList();
				var interestHit = role.Interests.Any(i => interests.Contains(Norm(i)));

				var fit = needsProfile
					? 0
					: (int)Math.Round(80.0 * have.Count / role.Skills.Length + (interestHit ? 20 : 0));

				return new CareerPathDto(role.Id, role.Name, fit, have, missing);
			})
			.OrderByDescending(p => p.Fit)
			.ThenBy(p => p.Name)
			.ToList();

		return new CareerPathsResponse(needsProfile, paths);
	}

	private static HashSet<string> Set(IEnumerable<string>? items) =>
		(items ?? []).Select(Norm).ToHashSet();

	private static string Norm(string value) => value.Trim().ToLowerInvariant();
}
