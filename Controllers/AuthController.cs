using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using LogiTrack.Models;
using LogiTrack.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace LogiTrack.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/auth")]
[EnableRateLimiting("auth")]
public class AuthController(UserManager<ApplicationUser> users,
    SignInManager<ApplicationUser> signIn, IOptions<JwtSettings> jwtOptions) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request)
    {
        var email = request.Email.Trim();
        var user = new ApplicationUser { UserName = email, Email = email };
        // Registration never accepts a role or an Identity entity from the caller.
        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded)
            return Problem(statusCode: 400,
                detail: "Registration failed. Use an unused email and a password of 12–128 characters with uppercase, lowercase, a digit, and a symbol.");

        return StatusCode(StatusCodes.Status201Created, new { user.Id, user.Email });
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request)
    {
        var user = await users.FindByEmailAsync(request.Email.Trim());
        if (user is null)
            return InvalidCredentials();

        var result = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
        if (!result.Succeeded || await users.GetTwoFactorEnabledAsync(user))
            return InvalidCredentials();

        var settings = jwtOptions.Value;
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(settings.LifetimeMinutes);
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new(JwtRegisteredClaimNames.Iat, new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };
        claims.AddRange((await users.GetRolesAsync(user)).Select(role => new Claim("role", role)));
        var token = new JwtSecurityToken(settings.Issuer, settings.Audience, claims,
            notBefore: now, expires: expires,
            signingCredentials: new SigningCredentials(settings.GetSigningKey(), SecurityAlgorithms.HmacSha256));

        Response.Headers.CacheControl = "no-store";
        Response.Headers.Pragma = "no-cache";
        return Ok(new { accessToken = new JwtSecurityTokenHandler().WriteToken(token), tokenType = "Bearer", expiresAtUtc = expires });
    }

    private ObjectResult InvalidCredentials() => Problem(statusCode: 401, detail: "Invalid email or password.");
}
