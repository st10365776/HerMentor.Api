using Microsoft.EntityFrameworkCore;

namespace HerMentor.Api.Data;

public static class Seed
{
	public static async Task RunAsync(AppDb db)
	{
		await db.Database.EnsureCreatedAsync();

		// EnsureCreated skips existing databases, so add newer tables and indexes without wiping them.
		await db.Database.ExecuteSqlRawAsync("""
			IF OBJECT_ID(N'[Sessions]', N'U') IS NULL
			BEGIN
				CREATE TABLE [Sessions] (
					[Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_Sessions] PRIMARY KEY,
					[MenteeId] int NOT NULL,
					[MentorId] int NOT NULL,
					[StartsAt] datetime2 NOT NULL,
					[Format] nvarchar(max) NOT NULL,
					[Topic] nvarchar(max) NOT NULL,
					[Goal] nvarchar(max) NOT NULL,
					[Status] nvarchar(max) NOT NULL,
					[PrepJson] nvarchar(max) NULL,
					[Summary] nvarchar(max) NOT NULL,
					[MentorFeedback] nvarchar(max) NOT NULL,
					[CreatedAt] datetime2 NOT NULL
				);
			END
			""");
		await db.Database.ExecuteSqlRawAsync("""
			IF OBJECT_ID(N'[SessionActions]', N'U') IS NULL
			BEGIN
				CREATE TABLE [SessionActions] (
					[Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_SessionActions] PRIMARY KEY,
					[SessionId] int NOT NULL,
					[Text] nvarchar(max) NOT NULL,
					[Done] bit NOT NULL,
					CONSTRAINT [FK_SessionActions_Sessions_SessionId] FOREIGN KEY ([SessionId]) REFERENCES [Sessions] ([Id]) ON DELETE CASCADE
				);
			END
			""");
		await db.Database.ExecuteSqlRawAsync("""
			IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_Sessions_MenteeId' AND [object_id] = OBJECT_ID(N'[Sessions]'))
				CREATE INDEX [IX_Sessions_MenteeId] ON [Sessions] ([MenteeId]);
			IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_Sessions_MentorId_StartsAt' AND [object_id] = OBJECT_ID(N'[Sessions]'))
				CREATE UNIQUE INDEX [IX_Sessions_MentorId_StartsAt] ON [Sessions] ([MentorId], [StartsAt]) WHERE [Status] <> N'Cancelled';
			IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_SessionActions_SessionId' AND [object_id] = OBJECT_ID(N'[SessionActions]'))
				CREATE INDEX [IX_SessionActions_SessionId] ON [SessionActions] ([SessionId]);
			""");
		await db.Database.ExecuteSqlRawAsync("""
			IF OBJECT_ID(N'[Messages]', N'U') IS NULL
			BEGIN
				CREATE TABLE [Messages] (
					[Id] int IDENTITY(1,1) NOT NULL CONSTRAINT [PK_Messages] PRIMARY KEY,
					[SessionId] int NOT NULL,
					[SenderId] int NOT NULL,
					[Content] nvarchar(2000) NOT NULL,
					[CreatedAt] datetime2 NOT NULL CONSTRAINT [DF_Messages_CreatedAt] DEFAULT SYSUTCDATETIME(),
					CONSTRAINT [FK_Messages_Sessions_SessionId] FOREIGN KEY ([SessionId]) REFERENCES [Sessions] ([Id]) ON DELETE CASCADE,
					CONSTRAINT [FK_Messages_Users_SenderId] FOREIGN KEY ([SenderId]) REFERENCES [Users] ([Id])
				);
				CREATE INDEX [IX_Messages_SessionId_Id] ON [Messages] ([SessionId], [Id]);
			END
			""");

		if (await db.Mentors.AnyAsync())
			return;

		db.Mentors.AddRange(
			New("Naledi Mokoena", "Senior Engineer, Thutuka", "Tech", 8, 4.9, "Weekday evenings",
				"I help women break into tech by building real skills, strong portfolios and the confidence to interview well.",
				"Full-stack", "React", "Node.js", "Career switch"),
			New("Amara Dlamini", "Product Designer, Orbit", "Design", 6, 4.8, "Weekends",
				"I mentor designers on building a portfolio, running user research and landing their first product role.",
				"UX design", "Figma", "Portfolio"),
			New("Lerato Pillay", "Data Lead, Insight Co", "Data", 10, 4.9, "Tuesday and Thursday",
				"I guide aspiring analysts and data scientists from first SQL query to a production model.",
				"AI / Data", "SQL", "Python"),
			New("Thabo Mokwena", "Cloud Architect, Nimbus", "Tech", 12, 4.7, "Weekday mornings",
				"I help engineers learn cloud fundamentals, design reliable systems and prepare for certifications.",
				"Cloud", "Node.js", "DevOps"),
			New("Zanele Khumalo", "Engineering Manager, Kasi Labs", "Leadership", 11, 5.0, "Fridays",
				"I coach engineers into leadership: managing people, influencing without authority and growing a team.",
				"Leadership", "Full-stack", "Interviews"),
			New("Priya Naidoo", "UX Researcher, Loop", "Design", 5, 4.6, "Weekday evenings",
				"I teach research methods and help designers turn insights into decisions.",
				"UX design", "Research", "Figma"),
			New("Nomsa Dube", "ML Engineer, Sable AI", "Data", 7, 4.8, "Weekends",
				"I support people moving into machine learning, from fundamentals to shipping their first model.",
				"AI / Data", "Python", "Cloud"),
			New("Ayanda Zulu", "Head of Product, Bridge", "Leadership", 14, 4.9, "By appointment",
				"I mentor future product leaders on strategy, stakeholder management and career switches into product.",
				"Leadership", "Career switch", "Product"));

		await db.SaveChangesAsync();
	}

	private static Mentor New(string name, string title, string category, int years, double rating, string availability, string bio, params string[] tags) => new()
	{
		Name = name,
		Title = title,
		Category = category,
		Years = years,
		Rating = rating,
		Availability = availability,
		Bio = bio,
		Tags = [.. tags],
		IsAvailable = true
	};
}
