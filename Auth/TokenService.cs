using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HerMentor.Api.Data;
using Microsoft.IdentityModel.Tokens;

namespace HerMentor.Api.Auth;

public sealed class TokenService(IConfiguration config)
{
	public const string Issuer = "hermentor-api";
	public const string Audience = "hermentor-app";

	public static SymmetricSecurityKey SigningKey(IConfiguration config) =>
		new(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));

	public string Create(User user)
	{
		var claims = new[]
		{
			new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
			new Claim(JwtRegisteredClaimNames.Email, user.Email),
			new Claim("name", user.Name),
			new Claim("role", user.Role)
		};

		var token = new JwtSecurityToken(
			Issuer,
			Audience,
			claims,
			expires: DateTime.UtcNow.AddDays(7),
			signingCredentials: new SigningCredentials(SigningKey(config), SecurityAlgorithms.HmacSha256));

		return new JwtSecurityTokenHandler().WriteToken(token);
	}
}

public static class ClaimsExtensions
{
	public static int UserId(this ClaimsPrincipal user) => int.Parse(user.FindFirstValue("sub")!);

	public static bool IsMentor(this ClaimsPrincipal user) => user.FindFirstValue("role") == "mentor";
}
