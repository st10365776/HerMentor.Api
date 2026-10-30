using System.Text;
using System.Threading.RateLimiting;
using HerMentor.Api.Ai;
using HerMentor.Api.Auth;
using HerMentor.Api.Data;
using HerMentor.Api.Endpoints;
using HerMentor.Api.Hubs;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

var jwtKey = builder.Configuration["Jwt:Key"];
if (string.IsNullOrWhiteSpace(jwtKey) || Encoding.UTF8.GetByteCount(jwtKey) < 32)
	throw new InvalidOperationException(
		"Jwt:Key is missing or shorter than 32 bytes. Set it with: dotnet user-secrets set \"Jwt:Key\" \"<long random string>\"");

builder.Services.AddDbContext<AppDb>(o =>
	o.UseSqlServer(builder.Configuration.GetConnectionString("Default")
		?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.")));
builder.Services.AddSingleton<TokenService>();
builder.Services.AddSignalR();

builder.Services.Configure<AiOptions>(builder.Configuration.GetSection("Ai"));
builder.Services.AddHttpClient<AiChat>(c => c.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddSingleton<AskLimiter>();

builder.Services
	.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
	.AddJwtBearer(o =>
	{
		o.MapInboundClaims = false;
		o.TokenValidationParameters = new TokenValidationParameters
		{
			ValidIssuer = TokenService.Issuer,
			ValidAudience = TokenService.Audience,
			IssuerSigningKey = TokenService.SigningKey(builder.Configuration),
			NameClaimType = "name",
			RoleClaimType = "role",
			ClockSkew = TimeSpan.FromMinutes(1)
		};

		// SignalR sends the token in the query string for WebSocket connections.
		o.Events = new JwtBearerEvents
		{
			OnMessageReceived = ctx =>
			{
				var token = ctx.Request.Query["access_token"];
				if (!string.IsNullOrEmpty(token) && ctx.HttpContext.Request.Path.StartsWithSegments("/hubs"))
					ctx.Token = token;
				return Task.CompletedTask;
			}
		};
	});
builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(o =>
{
	o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
	o.AddPolicy("auth", http => RateLimitPartition.GetFixedWindowLimiter(
		http.Connection.RemoteIpAddress?.ToString() ?? "unknown",
		_ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1) }));
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
	await Seed.RunAsync(scope.ServiceProvider.GetRequiredService<AppDb>());

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", () => "HerMentor API is running.");
app.MapAuth();
app.MapProfile();
app.MapMentors();
app.MapCareer();
app.MapPrep();
app.MapSessions();
app.MapMessages();
app.MapHub<MentorsHub>("/hubs/mentors");
app.MapHub<AssistantHub>("/hubs/assistant");

app.Run();
