using LogiTrack.Data;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers();
builder.Services.AddOpenApi();
builder.Services.AddDbContext<LogiTrackContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("LogiTrack")));
var app = builder.Build();
if (args.Contains("--demo"))
{
    using var scope = app.Services.CreateScope();
    await ActivityDemo.RunAsync(scope.ServiceProvider.GetRequiredService<LogiTrackContext>());
    return;
}
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
