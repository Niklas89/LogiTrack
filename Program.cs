using LogiTrack.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<LogiTrackContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("LogiTrack")));
var app = builder.Build();
if (args.Contains("--demo"))
{
    using var scope = app.Services.CreateScope();
    await ActivityDemo.RunAsync(scope.ServiceProvider.GetRequiredService<LogiTrackContext>());
    return;
}
app.MapGet("/", () => "LogiTrack Activity 1. Run with --demo to verify the database.");
app.Run();
