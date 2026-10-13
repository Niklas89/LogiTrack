using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace LogiTrack.Security;

public sealed class JwtSettings
{
    public string Issuer { get; set; } = "LogiTrack";
    public string Audience { get; set; } = "LogiTrack.Api";
    public string SigningKey { get; set; } = string.Empty;
    public int LifetimeMinutes { get; set; } = 15;

    public SymmetricSecurityKey GetSigningKey() => new(Encoding.UTF8.GetBytes(SigningKey));

    public TokenValidationParameters ValidationParameters() => new()
    {
        ValidateIssuer = true,
        ValidIssuer = Issuer,
        ValidateAudience = true,
        ValidAudience = Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = GetSigningKey(),
        ValidateLifetime = true,
        RequireExpirationTime = true,
        RequireSignedTokens = true,
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
        ClockSkew = TimeSpan.FromSeconds(30),
        NameClaimType = "sub",
        RoleClaimType = "role"
    };
}
