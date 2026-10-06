using Microsoft.EntityFrameworkCore;

namespace LanWatch.Server.Data;

public class LanWatchDb(DbContextOptions<LanWatchDb> options) : DbContext(options)
{
    public DbSet<TrafficMinute> TrafficMinutes => Set<TrafficMinute>();
    public DbSet<StatusMinute> StatusMinutes => Set<StatusMinute>();
    public DbSet<DownloadSession> Downloads => Set<DownloadSession>();
    public DbSet<Client> Clients => Set<Client>();
    public DbSet<ErrorEvent> Errors => Set<ErrorEvent>();
    public DbSet<StreamMinute> StreamMinutes => Set<StreamMinute>();
    public DbSet<LanEvent> Events => Set<LanEvent>();
    public DbSet<IngestCursor> IngestCursors => Set<IngestCursor>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<ExceptionSignOff> ExceptionSignOffs => Set<ExceptionSignOff>();
    public DbSet<SteamDepot> SteamDepots => Set<SteamDepot>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<TrafficMinute>(e =>
        {
            e.HasKey(x => new { x.Minute, x.ClientIp, x.Service });
            e.HasIndex(x => new { x.ClientIp, x.Minute });
        });

        b.Entity<StatusMinute>().HasKey(x => new { x.Minute, x.Service, x.Status });

        b.Entity<DownloadSession>(e =>
        {
            e.HasIndex(x => new { x.ClientIp, x.Service, x.ContentId, x.StartUnix }).IsUnique();
            e.HasIndex(x => x.LastUnix);
            e.HasIndex(x => new { x.Service, x.ContentId });
        });

        b.Entity<Client>().HasKey(x => x.Ip);

        b.Entity<ErrorEvent>(e =>
        {
            e.HasIndex(x => x.Unix);
            e.HasIndex(x => new { x.Kind, x.Unix });
        });

        b.Entity<StreamMinute>().HasKey(x => new { x.Minute, x.ClientIp, x.SniHost, x.Status });

        b.Entity<LanEvent>().HasIndex(x => x.StartUnix);

        b.Entity<IngestCursor>().HasKey(x => x.File);

        b.Entity<Setting>().HasKey(x => x.Key);

        b.Entity<ExceptionSignOff>().HasKey(x => new { x.Kind, x.Domain });

        b.Entity<SteamDepot>(e =>
        {
            e.HasKey(x => x.DepotId);
            e.Property(x => x.DepotId).ValueGeneratedNever();
        });
    }
}
