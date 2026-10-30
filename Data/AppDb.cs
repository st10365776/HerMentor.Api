using Microsoft.EntityFrameworkCore;

namespace HerMentor.Api.Data;

public class AppDb(DbContextOptions<AppDb> options) : DbContext(options)
{
	public DbSet<User> Users => Set<User>();
	public DbSet<MenteeProfile> MenteeProfiles => Set<MenteeProfile>();
	public DbSet<Mentor> Mentors => Set<Mentor>();
	public DbSet<Session> Sessions => Set<Session>();
	public DbSet<SessionAction> SessionActions => Set<SessionAction>();

	protected override void OnModelCreating(ModelBuilder b)
	{
		b.Entity<User>().HasIndex(u => u.Email).IsUnique();

		b.Entity<MenteeProfile>().HasKey(p => p.UserId);
		b.Entity<MenteeProfile>()
			.HasOne<User>()
			.WithOne(u => u.Profile)
			.HasForeignKey<MenteeProfile>(p => p.UserId);

		b.Entity<Mentor>().HasIndex(m => m.UserId).IsUnique();

		b.Entity<Session>().HasMany(s => s.Actions).WithOne().HasForeignKey(a => a.SessionId).OnDelete(DeleteBehavior.Cascade);
		b.Entity<Session>().HasIndex(s => s.MenteeId);
		// A mentor can't be double-booked unless the earlier booking was cancelled.
		b.Entity<Session>()
			.HasIndex(s => new { s.MentorId, s.StartsAt })
			.HasFilter("\"Status\" <> 'Cancelled'")
			.IsUnique();
	}
}
