using LogiTrack.Data;
using Microsoft.EntityFrameworkCore;
using System.Text;
using System.Threading.RateLimiting;
using LogiTrack.Models;
using LogiTrack.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddOpenApi(options => options.AddDocumentTransformer<BearerSecurityDocumentTransformer>());
builder.Services.AddDbContext<LogiTrackContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("LogiTrack")));
builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Password.RequiredLength = 12;
    options.Lockout.MaxFailedAccessAttempts = 5;
    options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
}).AddEntityFrameworkStores<LogiTrackContext>().AddDefaultTokenProviders();
builder.Services.AddOptions<JwtSettings>().BindConfiguration("Jwt")
    .Validate(settings => Encoding.UTF8.GetByteCount(settings.SigningKey) >= 32,
        "Set Jwt:SigningKey using user secrets or Jwt__SigningKey (at least 32 random bytes).")
    .Validate(settings => !string.IsNullOrWhiteSpace(settings.Issuer) && !string.IsNullOrWhiteSpace(settings.Audience),
        "JWT issuer and audience are required.")
    .Validate(settings => settings.LifetimeMinutes is >= 1 and <= 60, "JWT lifetime must be 1–60 minutes.")
    .ValidateOnStart();
// Override Identity's cookie defaults: API challenges must return 401/403, not redirects.
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultForbidScheme = JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtSettings>>((options, settings) =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = settings.Value.ValidationParameters();
    });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0
        }));
});
var app = builder.Build();
if (args.Contains("--grant-manager"))
{
    var index = Array.IndexOf(args, "--grant-manager");
    if (index + 1 >= args.Length) throw new ArgumentException("Usage: --grant-manager user@example.com");
    await ManagerRoleCommand.RunAsync(app.Services, args[index + 1]);
    return;
}
if (args.Contains("--demo"))
{
    using var scope = app.Services.CreateScope();
    await ActivityDemo.RunAsync(scope.ServiceProvider.GetRequiredService<LogiTrackContext>());
    return;
}
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler(handler => handler.Run(async context =>
        await Results.Problem(statusCode: 500, detail: "An unexpected error occurred.").ExecuteAsync(context)));
    app.UseHsts();
    app.UseHttpsRedirection();
}
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapGet("/swagger", () => Results.Content("""
        <!doctype html><html><head><title>LogiTrack API</title>
        <link rel="stylesheet" href="https://unpkg.com/swagger-ui-dist@5/swagger-ui.css"></head>
        <body><div id="swagger-ui"></div>
        <script src="https://unpkg.com/swagger-ui-dist@5/swagger-ui-bundle.js"></script>
        <script>SwaggerUIBundle({url:'/openapi/v1.json',dom_id:'#swagger-ui'});</script>
        </body></html>
        """, "text/html"));
}
app.MapGet("/", () => "LogiTrack API. Development documentation: /swagger");
app.Run();

public partial class Program;
