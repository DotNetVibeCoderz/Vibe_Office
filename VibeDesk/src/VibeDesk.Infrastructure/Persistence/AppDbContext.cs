using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using VibeDesk.Domain.Entities;
using VibeDesk.Infrastructure.Identity;

namespace VibeDesk.Infrastructure.Persistence;

/// <summary>
/// The single EF Core context, shared by all four database providers. Anything provider-specific is
/// resolved at model-build time from <see cref="DatabaseFacade.ProviderName"/> rather than being
/// hard-coded, because the same migrations have to work on SQLite, SQL Server, MySQL and PostgreSQL.
/// </summary>
public class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<AppUser, AppRole, Guid>(options)
{
    public DbSet<DriveItem> DriveItems => Set<DriveItem>();
    public DbSet<DriveItemContent> DriveItemContents => Set<DriveItemContent>();
    public DbSet<DriveItemVersion> DriveItemVersions => Set<DriveItemVersion>();
    public DbSet<DriveItemPermission> DriveItemPermissions => Set<DriveItemPermission>();
    public DbSet<Comment> Comments => Set<Comment>();

    public DbSet<Calendar> Calendars => Set<Calendar>();
    public DbSet<CalendarShare> CalendarShares => Set<CalendarShare>();
    public DbSet<CalendarEvent> CalendarEvents => Set<CalendarEvent>();
    public DbSet<EventAttendee> EventAttendees => Set<EventAttendee>();
    public DbSet<EventReminder> EventReminders => Set<EventReminder>();
    public DbSet<EventAttachment> EventAttachments => Set<EventAttachment>();

    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<ChatAttachment> ChatAttachments => Set<ChatAttachment>();

    public DbSet<Script> Scripts => Set<Script>();
    public DbSet<ScriptVersion> ScriptVersions => Set<ScriptVersion>();
    public DbSet<ScriptTrigger> ScriptTriggers => Set<ScriptTrigger>();
    public DbSet<ScriptRun> ScriptRuns => Set<ScriptRun>();
    public DbSet<ScriptPublication> ScriptPublications => Set<ScriptPublication>();

    public DbSet<ActivityEntry> ActivityEntries => Set<ActivityEntry>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<ApiKey> ApiKeys => Set<ApiKey>();
    public DbSet<AddOn> AddOns => Set<AddOn>();
    public DbSet<SyncState> SyncStates => Set<SyncState>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        var isSqlite = Database.ProviderName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true;
        var isSqlServer = Database.ProviderName?.Contains("SqlServer", StringComparison.OrdinalIgnoreCase) == true;

        ConfigureIdentity(builder);
        ConfigureDrive(builder, isSqlite, isSqlServer);
        ConfigureCalendar(builder);
        ConfigureChat(builder);
        ConfigurePlatform(builder);
        ConfigureScripting(builder);

        ApplyCrossProviderConventions(builder, isSqlite);
    }

    private static void ConfigureIdentity(ModelBuilder builder)
    {
        builder.Entity<AppUser>(e =>
        {
            e.ToTable("Users");
            e.Property(u => u.DisplayName).HasMaxLength(128).IsRequired();
            e.Property(u => u.AvatarColor).HasMaxLength(16);
            e.Property(u => u.AvatarKey).HasMaxLength(512);
            e.Property(u => u.JobTitle).HasMaxLength(128);
            e.Property(u => u.TimeZoneId).HasMaxLength(64);
            e.Property(u => u.Locale).HasMaxLength(8);
            e.Property(u => u.ThemePreference).HasMaxLength(16);

            // Not mapped: computed from DisplayName.
            e.Ignore(u => u.Initials);
        });

        builder.Entity<AppRole>(e =>
        {
            e.ToTable("Roles");
            e.Property(r => r.Description).HasMaxLength(256);
        });

        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserClaim<Guid>>().ToTable("UserClaims");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserLogin<Guid>>().ToTable("UserLogins");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserToken<Guid>>().ToTable("UserTokens");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityUserRole<Guid>>().ToTable("UserRoles");
        builder.Entity<Microsoft.AspNetCore.Identity.IdentityRoleClaim<Guid>>().ToTable("RoleClaims");
    }

    private static void ConfigureDrive(ModelBuilder builder, bool isSqlite, bool isSqlServer)
    {
        builder.Entity<DriveItem>(e =>
        {
            e.ToTable("DriveItems");
            e.HasKey(x => x.Id);

            e.Property(x => x.Name).HasMaxLength(512).IsRequired();
            e.Property(x => x.Path).HasMaxLength(1024).IsRequired();
            e.Property(x => x.StorageKey).HasMaxLength(1024);
            e.Property(x => x.ContentType).HasMaxLength(256);
            e.Property(x => x.ShareToken).HasMaxLength(64);
            e.Property(x => x.Color).HasMaxLength(16);
            e.Property(x => x.SearchText).HasMaxLength(4000);

            e.HasOne(x => x.Parent)
                .WithMany(x => x.Children)
                .HasForeignKey(x => x.ParentId)
                // A folder delete is handled explicitly (subtree walk + blob cleanup), so the database
                // must not cascade behind our back and orphan storage keys.
                .OnDelete(DeleteBehavior.Restrict);

            // The workhorse index: "children of this folder that aren't trashed", newest first.
            e.HasIndex(x => new { x.ParentId, x.IsTrashed, x.UpdatedAt });

            // Backs "my drive", "recent" and quota aggregation.
            e.HasIndex(x => new { x.OwnerId, x.IsTrashed, x.UpdatedAt });

            // Prefix-matched for subtree queries and inherited-permission resolution.
            e.HasIndex(x => x.Path);

            // Left unfiltered on purpose: EF's providers already exclude NULLs from a unique index on
            // a nullable column, and most items have no share token.
            e.HasIndex(x => x.ShareToken).IsUnique();
            e.HasIndex(x => new { x.OwnerId, x.IsStarred });

            ConfigureRowVersion(e, isSqlite, isSqlServer);
        });

        builder.Entity<DriveItemContent>(e =>
        {
            e.ToTable("DriveItemContents");
            // 1:1 with the item, keyed by the item id — the payload has no identity of its own.
            e.HasKey(x => x.DriveItemId);

            e.HasOne(x => x.DriveItem)
                .WithOne(x => x.Content)
                .HasForeignKey<DriveItemContent>(x => x.DriveItemId)
                .OnDelete(DeleteBehavior.Cascade);

            // Payloads routinely exceed any sane varchar limit, so force the provider's max text type.
            e.Property(x => x.Data).IsRequired();
            e.Property(x => x.PlainText);
        });

        builder.Entity<DriveItemVersion>(e =>
        {
            e.ToTable("DriveItemVersions");
            e.HasKey(x => x.Id);

            e.Property(x => x.Label).HasMaxLength(256);
            e.Property(x => x.StorageKey).HasMaxLength(1024);

            e.HasOne(x => x.DriveItem)
                .WithMany(x => x.Versions)
                .HasForeignKey(x => x.DriveItemId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new { x.DriveItemId, x.VersionNumber }).IsUnique();
        });

        builder.Entity<DriveItemPermission>(e =>
        {
            e.ToTable("DriveItemPermissions");
            e.HasKey(x => x.Id);

            e.Property(x => x.Email).HasMaxLength(320);

            e.HasOne(x => x.DriveItem)
                .WithMany(x => x.Permissions)
                .HasForeignKey(x => x.DriveItemId)
                .OnDelete(DeleteBehavior.Cascade);

            // Permission resolution always filters by user then item.
            e.HasIndex(x => new { x.UserId, x.DriveItemId });
            e.HasIndex(x => x.Email);
            e.HasIndex(x => x.DriveItemId);
        });

        builder.Entity<Comment>(e =>
        {
            e.ToTable("Comments");
            e.HasKey(x => x.Id);

            e.Property(x => x.Body).IsRequired();
            e.Property(x => x.Anchor).HasMaxLength(512);
            e.Property(x => x.MentionedUserIds).HasMaxLength(2000);

            e.HasOne(x => x.DriveItem)
                .WithMany(x => x.Comments)
                .HasForeignKey(x => x.DriveItemId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.ParentComment)
                .WithMany(x => x.Replies)
                .HasForeignKey(x => x.ParentCommentId)
                // Replies are deleted explicitly with their thread; cascade here would need a
                // self-referencing cascade path, which SQL Server rejects outright.
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => new { x.DriveItemId, x.Status });
        });
    }

    private static void ConfigureCalendar(ModelBuilder builder)
    {
        builder.Entity<Calendar>(e =>
        {
            e.ToTable("Calendars");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(256).IsRequired();
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.Color).HasMaxLength(16);
            e.Property(x => x.TimeZoneId).HasMaxLength(64);
            e.Property(x => x.ExternalId).HasMaxLength(256);

            e.HasIndex(x => new { x.OwnerId, x.IsPrimary });
        });

        builder.Entity<CalendarShare>(e =>
        {
            e.ToTable("CalendarShares");
            e.HasKey(x => x.Id);

            e.HasOne(x => x.Calendar)
                .WithMany(x => x.Shares)
                .HasForeignKey(x => x.CalendarId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new { x.UserId, x.CalendarId }).IsUnique();
        });

        builder.Entity<CalendarEvent>(e =>
        {
            e.ToTable("CalendarEvents");
            e.HasKey(x => x.Id);

            e.Property(x => x.Title).HasMaxLength(512).IsRequired();
            e.Property(x => x.Description).HasMaxLength(8000);
            e.Property(x => x.Location).HasMaxLength(512);
            e.Property(x => x.ColorOverride).HasMaxLength(16);
            e.Property(x => x.RecurrenceByDay).HasMaxLength(32);
            e.Property(x => x.SourceRange).HasMaxLength(64);

            e.HasOne(x => x.Calendar)
                .WithMany(x => x.Events)
                .HasForeignKey(x => x.CalendarId)
                .OnDelete(DeleteBehavior.Cascade);

            // The grid query is always "events in this calendar overlapping a window".
            e.HasIndex(x => new { x.CalendarId, x.StartUtc, x.EndUtc });
            e.HasIndex(x => x.RecurringSeriesId);
        });

        builder.Entity<EventAttendee>(e =>
        {
            e.ToTable("EventAttendees");
            e.HasKey(x => x.Id);
            e.Property(x => x.Email).HasMaxLength(320).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(128);

            e.HasOne(x => x.CalendarEvent)
                .WithMany(x => x.Attendees)
                .HasForeignKey(x => x.CalendarEventId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new { x.CalendarEventId, x.Email });
            // Backs "events I'm invited to" and free/busy probes.
            e.HasIndex(x => x.UserId);
        });

        builder.Entity<EventReminder>(e =>
        {
            e.ToTable("EventReminders");
            e.HasKey(x => x.Id);

            e.HasOne(x => x.CalendarEvent)
                .WithMany(x => x.Reminders)
                .HasForeignKey(x => x.CalendarEventId)
                .OnDelete(DeleteBehavior.Cascade);

            // The reminder sweeper scans for undispatched reminders only.
            e.HasIndex(x => x.SentAt);
        });

        builder.Entity<EventAttachment>(e =>
        {
            e.ToTable("EventAttachments");
            e.HasKey(x => x.Id);

            e.HasOne(x => x.CalendarEvent)
                .WithMany(x => x.Attachments)
                .HasForeignKey(x => x.CalendarEventId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.DriveItem)
                .WithMany()
                .HasForeignKey(x => x.DriveItemId)
                // Detaching is explicit; deleting a Drive file must not silently delete event rows.
                .OnDelete(DeleteBehavior.Restrict);

            e.HasIndex(x => new { x.CalendarEventId, x.DriveItemId }).IsUnique();
        });
    }

    private static void ConfigureChat(ModelBuilder builder)
    {
        builder.Entity<ChatSession>(e =>
        {
            e.ToTable("ChatSessions");
            e.HasKey(x => x.Id);

            e.Property(x => x.Title).HasMaxLength(256).IsRequired();
            e.Property(x => x.AppContext).HasMaxLength(32);
            e.Property(x => x.Model).HasMaxLength(128);
            e.Property(x => x.SystemPromptOverride).HasMaxLength(8000);

            // Session list is "mine, most recent first".
            e.HasIndex(x => new { x.UserId, x.IsArchived, x.UpdatedAt });
        });

        builder.Entity<ChatMessage>(e =>
        {
            e.ToTable("ChatMessages");
            e.HasKey(x => x.Id);

            e.Property(x => x.Content).IsRequired();
            e.Property(x => x.ModelUsed).HasMaxLength(128);
            e.Property(x => x.Error).HasMaxLength(2000);

            e.HasOne(x => x.ChatSession)
                .WithMany(x => x.Messages)
                .HasForeignKey(x => x.ChatSessionId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasIndex(x => new { x.ChatSessionId, x.CreatedAt });
        });

        builder.Entity<ChatAttachment>(e =>
        {
            e.ToTable("ChatAttachments");
            e.HasKey(x => x.Id);

            e.Property(x => x.FileName).HasMaxLength(512).IsRequired();
            e.Property(x => x.ContentType).HasMaxLength(256);
            e.Property(x => x.StorageKey).HasMaxLength(1024).IsRequired();
            e.Property(x => x.Url).HasMaxLength(2048).IsRequired();

            e.HasOne(x => x.ChatMessage)
                .WithMany(x => x.Attachments)
                .HasForeignKey(x => x.ChatMessageId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigureScripting(ModelBuilder builder)
    {
        builder.Entity<Script>(e =>
        {
            e.ToTable("Scripts");
            e.Property(x => x.Name).HasMaxLength(160).IsRequired();
            e.Property(x => x.Description).HasMaxLength(1024);
            e.Property(x => x.AllowedHosts).HasMaxLength(1024);
            e.Property(x => x.TemplateId).HasMaxLength(64);

            // The script list is always "mine, most recently touched first".
            e.HasIndex(x => new { x.OwnerId, x.UpdatedAt });

            e.HasMany(x => x.Versions).WithOne(v => v.Script!)
                .HasForeignKey(v => v.ScriptId).OnDelete(DeleteBehavior.Cascade);

            e.HasMany(x => x.Triggers).WithOne(t => t.Script!)
                .HasForeignKey(t => t.ScriptId).OnDelete(DeleteBehavior.Cascade);

            e.HasMany(x => x.Runs).WithOne(r => r.Script!)
                .HasForeignKey(r => r.ScriptId).OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<ScriptVersion>(e =>
        {
            e.ToTable("ScriptVersions");
            e.Property(x => x.Note).HasMaxLength(512);
            e.HasIndex(x => new { x.ScriptId, x.VersionNumber });
        });

        builder.Entity<ScriptTrigger>(e =>
        {
            e.ToTable("ScriptTriggers");
            e.Property(x => x.EventName).HasMaxLength(64);
            e.Property(x => x.CronExpression).HasMaxLength(128);
            e.Property(x => x.TimeZoneId).HasMaxLength(64);

            // The scheduler asks "what is due" every tick, and the dispatcher asks "who wants this
            // event" on every workspace action. Both are covered here.
            e.HasIndex(x => new { x.Kind, x.IsEnabled, x.NextRunAt });
            e.HasIndex(x => new { x.EventName, x.IsEnabled });
        });

        builder.Entity<ScriptRun>(e =>
        {
            e.ToTable("ScriptRuns");
            e.Property(x => x.TriggerDetail).HasMaxLength(512);
            e.HasIndex(x => new { x.ScriptId, x.StartedAt });
            e.HasIndex(x => x.StartedAt);
        });

        builder.Entity<ScriptPublication>(e =>
        {
            e.ToTable("ScriptPublications");
            e.Property(x => x.Name).HasMaxLength(160).IsRequired();
            e.Property(x => x.Summary).HasMaxLength(1024);
            e.Property(x => x.Category).HasMaxLength(64);
            e.Property(x => x.AllowedHosts).HasMaxLength(1024);
            e.HasIndex(x => new { x.IsListed, x.PublishedAt });
        });
    }

    private static void ConfigurePlatform(ModelBuilder builder)
    {
        builder.Entity<ActivityEntry>(e =>
        {
            e.ToTable("ActivityEntries");
            e.HasKey(x => x.Id);
            e.Property(x => x.Action).HasMaxLength(64).IsRequired();
            e.Property(x => x.Detail).HasMaxLength(1000);
            e.Property(x => x.IpAddress).HasMaxLength(64);

            e.HasIndex(x => new { x.DriveItemId, x.OccurredAt });
            e.HasIndex(x => new { x.ActorId, x.OccurredAt });
        });

        builder.Entity<Notification>(e =>
        {
            e.ToTable("Notifications");
            e.HasKey(x => x.Id);
            e.Property(x => x.Title).HasMaxLength(256).IsRequired();
            e.Property(x => x.Body).HasMaxLength(2000);
            e.Property(x => x.Link).HasMaxLength(512);

            e.HasIndex(x => new { x.UserId, x.IsRead, x.CreatedAt });
        });

        builder.Entity<ApiKey>(e =>
        {
            e.ToTable("ApiKeys");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(128).IsRequired();
            e.Property(x => x.Prefix).HasMaxLength(32).IsRequired();
            e.Property(x => x.KeyHash).HasMaxLength(128).IsRequired();
            e.Property(x => x.Scopes).HasMaxLength(512);

            e.Ignore(x => x.IsActive);

            // Validation looks the key up by hash on every API request.
            e.HasIndex(x => x.KeyHash).IsUnique();
            e.HasIndex(x => x.OwnerId);
        });

        builder.Entity<AddOn>(e =>
        {
            e.ToTable("AddOns");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(128).IsRequired();
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.Publisher).HasMaxLength(128);
            e.Property(x => x.IconEmoji).HasMaxLength(16);
            e.Property(x => x.Surfaces).HasMaxLength(128);
            e.Property(x => x.Scopes).HasMaxLength(512);
            e.Property(x => x.WebhookUrl).HasMaxLength(512);
        });

        builder.Entity<SyncState>(e =>
        {
            e.ToTable("SyncStates");
            e.HasKey(x => x.Id);
            e.Property(x => x.DeviceId).HasMaxLength(128).IsRequired();
            e.Property(x => x.DeviceName).HasMaxLength(128);
            e.Property(x => x.Platform).HasMaxLength(32);

            e.HasIndex(x => new { x.UserId, x.DeviceId }).IsUnique();
        });
    }

    /// <summary>
    /// Concurrency token, which is the one place the four providers genuinely diverge:
    /// SQL Server has a native <c>rowversion</c>, the others need a client-maintained token.
    /// </summary>
    private static void ConfigureRowVersion<T>(
        Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<T> entity,
        bool isSqlite,
        bool isSqlServer)
        where T : class
    {
        var property = entity.Property<byte[]>(nameof(DriveItem.RowVersion));

        if (isSqlServer)
        {
            property.IsRowVersion();
            return;
        }

        // SQLite and MySQL/PostgreSQL: a plain byte[] we stamp ourselves in SaveChanges.
        property.IsConcurrencyToken().HasMaxLength(16);
        _ = isSqlite;
    }

    /// <summary>
    /// Conventions that keep one set of migrations valid across providers: <see cref="DateTimeOffset"/>
    /// is not natively supported by SQLite or MySQL, and unbounded strings need the provider's
    /// large-text type rather than a default-length varchar.
    /// </summary>
    private static void ApplyCrossProviderConventions(ModelBuilder builder, bool isSqlite)
    {
        foreach (var entity in builder.Model.GetEntityTypes())
        {
            foreach (var property in entity.GetProperties())
            {
                if (isSqlite && property.ClrType == typeof(DateTimeOffset))
                {
                    property.SetValueConverter(DateTimeOffsetToTicksConverter.Instance);
                }
                else if (isSqlite && property.ClrType == typeof(DateTimeOffset?))
                {
                    property.SetValueConverter(NullableDateTimeOffsetToTicksConverter.Instance);
                }
            }
        }
    }

    /// <summary>
    /// Stamps the client-maintained concurrency token on modified Drive items. SQL Server overwrites
    /// this with its native rowversion, so doing it unconditionally is harmless there.
    /// </summary>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampConcurrencyTokens();
        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        StampConcurrencyTokens();
        return base.SaveChanges();
    }

    private void StampConcurrencyTokens()
    {
        foreach (var entry in ChangeTracker.Entries<DriveItem>())
        {
            if (entry.State is EntityState.Added or EntityState.Modified)
            {
                entry.Entity.RowVersion = Guid.NewGuid().ToByteArray();
            }
        }
    }
}
