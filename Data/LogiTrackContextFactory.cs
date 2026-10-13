using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace LogiTrack.Data;

// Migrations do not need the API's JWT signing secret.
public class LogiTrackContextFactory : IDesignTimeDbContextFactory<LogiTrackContext>
{
    public LogiTrackContext CreateDbContext(string[] args)
    {
        var configuration = new ConfigurationBuilder().SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json").AddEnvironmentVariables().Build();
        var options = new DbContextOptionsBuilder<LogiTrackContext>()
            .UseSqlServer(configuration.GetConnectionString("LogiTrack")).Options;
        return new LogiTrackContext(options);
    }
}
