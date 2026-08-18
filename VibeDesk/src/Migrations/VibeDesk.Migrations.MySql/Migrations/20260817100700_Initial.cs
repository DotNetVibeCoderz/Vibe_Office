using System;
using Microsoft.EntityFrameworkCore.Migrations;
using MySql.EntityFrameworkCore.Metadata;

#nullable disable

namespace VibeDesk.Migrations.MySql.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ActivityEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ActorId = table.Column<Guid>(type: "char(36)", nullable: true),
                    DriveItemId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Action = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Detail = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    IpAddress = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityEntries", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "AddOns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    Publisher = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                    IconEmoji = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true),
                    Surfaces = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    Scopes = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    WebhookUrl = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true),
                    IsEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AddOns", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ApiKeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    OwnerId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Name = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    Prefix = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    KeyHash = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    Scopes = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    LastUsedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    RevokedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiKeys", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Calendars",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    Color = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    OwnerId = table.Column<Guid>(type: "char(36)", nullable: false),
                    IsPrimary = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    TimeZoneId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    ExternalKind = table.Column<int>(type: "int", nullable: false),
                    ExternalId = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    LastSyncedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Calendars", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ChatSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Title = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    AppContext = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    DriveItemId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Provider = table.Column<int>(type: "int", nullable: true),
                    Model = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                    Temperature = table.Column<double>(type: "double", nullable: true),
                    SystemPromptOverride = table.Column<string>(type: "varchar(8000)", maxLength: 8000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    IsArchived = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatSessions", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "DriveItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    ParentId = table.Column<Guid>(type: "char(36)", nullable: true),
                    OwnerId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Path = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false),
                    StorageKey = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true),
                    ContentType = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    IsEncrypted = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Scope = table.Column<int>(type: "int", nullable: false),
                    ShareToken = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    LinkRole = table.Column<int>(type: "int", nullable: false),
                    IsStarred = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    IsTrashed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    TrashedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    SearchText = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true),
                    Color = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "char(36)", nullable: true),
                    LastViewedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "varbinary(16)", maxLength: 16, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriveItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DriveItems_DriveItems_ParentId",
                        column: x => x.ParentId,
                        principalTable: "DriveItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Notifications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Title = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    Body = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    Link = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true),
                    IsRead = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    ReadAt = table.Column<DateTimeOffset>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notifications", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    Description = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    Name = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "longtext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "SyncStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    DeviceId = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    DeviceName = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                    Platform = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    Cursor = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncStates", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    DisplayName = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    AvatarColor = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true),
                    AvatarKey = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true),
                    JobTitle = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                    TimeZoneId = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    Locale = table.Column<string>(type: "varchar(8)", maxLength: 8, nullable: false),
                    ThemePreference = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    QuotaBytes = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    IsDisabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    UserName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    PasswordHash = table.Column<string>(type: "longtext", nullable: true),
                    SecurityStamp = table.Column<string>(type: "longtext", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "longtext", nullable: true),
                    PhoneNumber = table.Column<string>(type: "longtext", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CalendarEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    CalendarId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Title = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    Description = table.Column<string>(type: "varchar(8000)", maxLength: 8000, nullable: true),
                    Location = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true),
                    StartUtc = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    EndUtc = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    IsAllDay = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Visibility = table.Column<int>(type: "int", nullable: false),
                    ColorOverride = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true),
                    Recurrence = table.Column<int>(type: "int", nullable: false),
                    RecurrenceInterval = table.Column<int>(type: "int", nullable: false),
                    RecurrenceUntilUtc = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    RecurrenceByDay = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true),
                    RecurringSeriesId = table.Column<Guid>(type: "char(36)", nullable: true),
                    OriginalStartUtc = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    IsCancelled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SourceSpreadsheetId = table.Column<Guid>(type: "char(36)", nullable: true),
                    SourceRange = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    OrganizerId = table.Column<Guid>(type: "char(36)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalendarEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CalendarEvents_Calendars_CalendarId",
                        column: x => x.CalendarId,
                        principalTable: "Calendars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CalendarShares",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    CalendarId = table.Column<Guid>(type: "char(36)", nullable: false),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalendarShares", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CalendarShares_Calendars_CalendarId",
                        column: x => x.CalendarId,
                        principalTable: "Calendars",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ChatMessages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ChatSessionId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Role = table.Column<int>(type: "int", nullable: false),
                    Content = table.Column<string>(type: "longtext", nullable: false),
                    ToolCallsJson = table.Column<string>(type: "longtext", nullable: true),
                    PromptTokens = table.Column<int>(type: "int", nullable: true),
                    CompletionTokens = table.Column<int>(type: "int", nullable: true),
                    ModelUsed = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                    ProviderUsed = table.Column<int>(type: "int", nullable: true),
                    Error = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatMessages_ChatSessions_ChatSessionId",
                        column: x => x.ChatSessionId,
                        principalTable: "ChatSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "Comments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    DriveItemId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ParentCommentId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<int>(type: "int", nullable: false),
                    Body = table.Column<string>(type: "longtext", nullable: false),
                    Anchor = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: true),
                    SuggestedText = table.Column<string>(type: "longtext", nullable: true),
                    OriginalText = table.Column<string>(type: "longtext", nullable: true),
                    AuthorId = table.Column<Guid>(type: "char(36)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    ResolvedById = table.Column<Guid>(type: "char(36)", nullable: true),
                    ResolvedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: true),
                    MentionedUserIds = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Comments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Comments_Comments_ParentCommentId",
                        column: x => x.ParentCommentId,
                        principalTable: "Comments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Comments_DriveItems_DriveItemId",
                        column: x => x.DriveItemId,
                        principalTable: "DriveItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "DriveItemContents",
                columns: table => new
                {
                    DriveItemId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Data = table.Column<string>(type: "longtext", nullable: false),
                    PlainText = table.Column<string>(type: "longtext", nullable: true),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    UpdatedById = table.Column<Guid>(type: "char(36)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriveItemContents", x => x.DriveItemId);
                    table.ForeignKey(
                        name: "FK_DriveItemContents_DriveItems_DriveItemId",
                        column: x => x.DriveItemId,
                        principalTable: "DriveItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "DriveItemPermissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    DriveItemId = table.Column<Guid>(type: "char(36)", nullable: false),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Email = table.Column<string>(type: "varchar(320)", maxLength: 320, nullable: true),
                    Role = table.Column<int>(type: "int", nullable: false),
                    GrantedById = table.Column<Guid>(type: "char(36)", nullable: false),
                    GrantedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriveItemPermissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DriveItemPermissions_DriveItems_DriveItemId",
                        column: x => x.DriveItemId,
                        principalTable: "DriveItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "DriveItemVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    DriveItemId = table.Column<Guid>(type: "char(36)", nullable: false),
                    VersionNumber = table.Column<int>(type: "int", nullable: false),
                    Data = table.Column<string>(type: "longtext", nullable: true),
                    StorageKey = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    CreatedById = table.Column<Guid>(type: "char(36)", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false),
                    Label = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: true),
                    IsAutoSnapshot = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DriveItemVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DriveItemVersions_DriveItems_DriveItemId",
                        column: x => x.DriveItemId,
                        principalTable: "DriveItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "RoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    RoleId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ClaimType = table.Column<string>(type: "longtext", nullable: true),
                    ClaimValue = table.Column<string>(type: "longtext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoleClaims_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "UserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySQL:ValueGenerationStrategy", MySQLValueGenerationStrategy.IdentityColumn),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    ClaimType = table.Column<string>(type: "longtext", nullable: true),
                    ClaimValue = table.Column<string>(type: "longtext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserClaims_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "UserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "varchar(255)", nullable: false),
                    ProviderKey = table.Column<string>(type: "varchar(255)", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "longtext", nullable: true),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_UserLogins_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "UserRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    RoleId = table.Column<Guid>(type: "char(36)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_UserRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserRoles_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "UserTokens",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "char(36)", nullable: false),
                    LoginProvider = table.Column<string>(type: "varchar(255)", nullable: false),
                    Name = table.Column<string>(type: "varchar(255)", nullable: false),
                    Value = table.Column<string>(type: "longtext", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_UserTokens_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EventAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    CalendarEventId = table.Column<Guid>(type: "char(36)", nullable: false),
                    DriveItemId = table.Column<Guid>(type: "char(36)", nullable: false),
                    GrantAttendees = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventAttachments_CalendarEvents_CalendarEventId",
                        column: x => x.CalendarEventId,
                        principalTable: "CalendarEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_EventAttachments_DriveItems_DriveItemId",
                        column: x => x.DriveItemId,
                        principalTable: "DriveItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EventAttendees",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    CalendarEventId = table.Column<Guid>(type: "char(36)", nullable: false),
                    UserId = table.Column<Guid>(type: "char(36)", nullable: true),
                    Email = table.Column<string>(type: "varchar(320)", maxLength: 320, nullable: false),
                    DisplayName = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                    Response = table.Column<int>(type: "int", nullable: false),
                    IsOptional = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    RespondedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventAttendees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventAttendees_CalendarEvents_CalendarEventId",
                        column: x => x.CalendarEventId,
                        principalTable: "CalendarEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "EventReminders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    CalendarEventId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Method = table.Column<int>(type: "int", nullable: false),
                    MinutesBefore = table.Column<int>(type: "int", nullable: false),
                    SentAt = table.Column<DateTimeOffset>(type: "datetime", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EventReminders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EventReminders_CalendarEvents_CalendarEventId",
                        column: x => x.CalendarEventId,
                        principalTable: "CalendarEvents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ChatAttachments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ChatMessageId = table.Column<Guid>(type: "char(36)", nullable: false),
                    Kind = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    ContentType = table.Column<string>(type: "varchar(256)", maxLength: 256, nullable: false),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    StorageKey = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: false),
                    Url = table.Column<string>(type: "varchar(2048)", maxLength: 2048, nullable: false),
                    ExtractedText = table.Column<string>(type: "longtext", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetime", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatAttachments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatAttachments_ChatMessages_ChatMessageId",
                        column: x => x.ChatMessageId,
                        principalTable: "ChatMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEntries_ActorId_OccurredAt",
                table: "ActivityEntries",
                columns: new[] { "ActorId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityEntries_DriveItemId_OccurredAt",
                table: "ActivityEntries",
                columns: new[] { "DriveItemId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_KeyHash",
                table: "ApiKeys",
                column: "KeyHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiKeys_OwnerId",
                table: "ApiKeys",
                column: "OwnerId");

            migrationBuilder.CreateIndex(
                name: "IX_CalendarEvents_CalendarId_StartUtc_EndUtc",
                table: "CalendarEvents",
                columns: new[] { "CalendarId", "StartUtc", "EndUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_CalendarEvents_RecurringSeriesId",
                table: "CalendarEvents",
                column: "RecurringSeriesId");

            migrationBuilder.CreateIndex(
                name: "IX_Calendars_OwnerId_IsPrimary",
                table: "Calendars",
                columns: new[] { "OwnerId", "IsPrimary" });

            migrationBuilder.CreateIndex(
                name: "IX_CalendarShares_CalendarId",
                table: "CalendarShares",
                column: "CalendarId");

            migrationBuilder.CreateIndex(
                name: "IX_CalendarShares_UserId_CalendarId",
                table: "CalendarShares",
                columns: new[] { "UserId", "CalendarId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatAttachments_ChatMessageId",
                table: "ChatAttachments",
                column: "ChatMessageId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_ChatSessionId_CreatedAt",
                table: "ChatMessages",
                columns: new[] { "ChatSessionId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatSessions_UserId_IsArchived_UpdatedAt",
                table: "ChatSessions",
                columns: new[] { "UserId", "IsArchived", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Comments_DriveItemId_Status",
                table: "Comments",
                columns: new[] { "DriveItemId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Comments_ParentCommentId",
                table: "Comments",
                column: "ParentCommentId");

            migrationBuilder.CreateIndex(
                name: "IX_DriveItemPermissions_DriveItemId",
                table: "DriveItemPermissions",
                column: "DriveItemId");

            migrationBuilder.CreateIndex(
                name: "IX_DriveItemPermissions_Email",
                table: "DriveItemPermissions",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_DriveItemPermissions_UserId_DriveItemId",
                table: "DriveItemPermissions",
                columns: new[] { "UserId", "DriveItemId" });

            migrationBuilder.CreateIndex(
                name: "IX_DriveItems_OwnerId_IsStarred",
                table: "DriveItems",
                columns: new[] { "OwnerId", "IsStarred" });

            migrationBuilder.CreateIndex(
                name: "IX_DriveItems_OwnerId_IsTrashed_UpdatedAt",
                table: "DriveItems",
                columns: new[] { "OwnerId", "IsTrashed", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DriveItems_ParentId_IsTrashed_UpdatedAt",
                table: "DriveItems",
                columns: new[] { "ParentId", "IsTrashed", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_DriveItems_Path",
                table: "DriveItems",
                column: "Path");

            migrationBuilder.CreateIndex(
                name: "IX_DriveItems_ShareToken",
                table: "DriveItems",
                column: "ShareToken",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DriveItemVersions_DriveItemId_VersionNumber",
                table: "DriveItemVersions",
                columns: new[] { "DriveItemId", "VersionNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventAttachments_CalendarEventId_DriveItemId",
                table: "EventAttachments",
                columns: new[] { "CalendarEventId", "DriveItemId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EventAttachments_DriveItemId",
                table: "EventAttachments",
                column: "DriveItemId");

            migrationBuilder.CreateIndex(
                name: "IX_EventAttendees_CalendarEventId_Email",
                table: "EventAttendees",
                columns: new[] { "CalendarEventId", "Email" });

            migrationBuilder.CreateIndex(
                name: "IX_EventAttendees_UserId",
                table: "EventAttendees",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_EventReminders_CalendarEventId",
                table: "EventReminders",
                column: "CalendarEventId");

            migrationBuilder.CreateIndex(
                name: "IX_EventReminders_SentAt",
                table: "EventReminders",
                column: "SentAt");

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_UserId_IsRead_CreatedAt",
                table: "Notifications",
                columns: new[] { "UserId", "IsRead", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RoleClaims_RoleId",
                table: "RoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "Roles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SyncStates_UserId_DeviceId",
                table: "SyncStates",
                columns: new[] { "UserId", "DeviceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserClaims_UserId",
                table: "UserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserLogins_UserId",
                table: "UserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_RoleId",
                table: "UserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "Users",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "Users",
                column: "NormalizedUserName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityEntries");

            migrationBuilder.DropTable(
                name: "AddOns");

            migrationBuilder.DropTable(
                name: "ApiKeys");

            migrationBuilder.DropTable(
                name: "CalendarShares");

            migrationBuilder.DropTable(
                name: "ChatAttachments");

            migrationBuilder.DropTable(
                name: "Comments");

            migrationBuilder.DropTable(
                name: "DriveItemContents");

            migrationBuilder.DropTable(
                name: "DriveItemPermissions");

            migrationBuilder.DropTable(
                name: "DriveItemVersions");

            migrationBuilder.DropTable(
                name: "EventAttachments");

            migrationBuilder.DropTable(
                name: "EventAttendees");

            migrationBuilder.DropTable(
                name: "EventReminders");

            migrationBuilder.DropTable(
                name: "Notifications");

            migrationBuilder.DropTable(
                name: "RoleClaims");

            migrationBuilder.DropTable(
                name: "SyncStates");

            migrationBuilder.DropTable(
                name: "UserClaims");

            migrationBuilder.DropTable(
                name: "UserLogins");

            migrationBuilder.DropTable(
                name: "UserRoles");

            migrationBuilder.DropTable(
                name: "UserTokens");

            migrationBuilder.DropTable(
                name: "ChatMessages");

            migrationBuilder.DropTable(
                name: "DriveItems");

            migrationBuilder.DropTable(
                name: "CalendarEvents");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "Users");

            migrationBuilder.DropTable(
                name: "ChatSessions");

            migrationBuilder.DropTable(
                name: "Calendars");
        }
    }
}
