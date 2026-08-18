using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VibeDesk.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class AddScripting : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ScriptPublications",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScriptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AuthorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Summary = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Category = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Language = table.Column<int>(type: "INTEGER", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Scopes = table.Column<int>(type: "INTEGER", nullable: false),
                    AllowedHosts = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    InstallCount = table.Column<int>(type: "INTEGER", nullable: false),
                    PublishedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    IsListed = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScriptPublications", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Scripts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OwnerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 160, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    Language = table.Column<int>(type: "INTEGER", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Scopes = table.Column<int>(type: "INTEGER", nullable: false),
                    AllowedHosts = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    TimeoutSeconds = table.Column<int>(type: "INTEGER", nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    TemplateId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    VersionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    LastRunAt = table.Column<long>(type: "INTEGER", nullable: true),
                    LastRunStatus = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Scripts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ScriptRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScriptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TriggeredBy = table.Column<int>(type: "INTEGER", nullable: false),
                    TriggerDetail = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    FinishedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    DurationMs = table.Column<int>(type: "INTEGER", nullable: false),
                    Output = table.Column<string>(type: "TEXT", nullable: true),
                    ResultJson = table.Column<string>(type: "TEXT", nullable: true),
                    Error = table.Column<string>(type: "TEXT", nullable: true),
                    ApiCallsJson = table.Column<string>(type: "TEXT", nullable: true),
                    VersionNumber = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScriptRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScriptRuns_Scripts_ScriptId",
                        column: x => x.ScriptId,
                        principalTable: "Scripts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScriptTriggers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScriptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    EventName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    DriveItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    CronExpression = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    TimeZoneId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    NextRunAt = table.Column<long>(type: "INTEGER", nullable: true),
                    LastFiredAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScriptTriggers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScriptTriggers_Scripts_ScriptId",
                        column: x => x.ScriptId,
                        principalTable: "Scripts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScriptVersions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    ScriptId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VersionNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    Code = table.Column<string>(type: "TEXT", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    AuthorId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScriptVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScriptVersions_Scripts_ScriptId",
                        column: x => x.ScriptId,
                        principalTable: "Scripts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ScriptPublications_IsListed_PublishedAt",
                table: "ScriptPublications",
                columns: new[] { "IsListed", "PublishedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ScriptRuns_ScriptId_StartedAt",
                table: "ScriptRuns",
                columns: new[] { "ScriptId", "StartedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ScriptRuns_StartedAt",
                table: "ScriptRuns",
                column: "StartedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Scripts_OwnerId_UpdatedAt",
                table: "Scripts",
                columns: new[] { "OwnerId", "UpdatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ScriptTriggers_EventName_IsEnabled",
                table: "ScriptTriggers",
                columns: new[] { "EventName", "IsEnabled" });

            migrationBuilder.CreateIndex(
                name: "IX_ScriptTriggers_Kind_IsEnabled_NextRunAt",
                table: "ScriptTriggers",
                columns: new[] { "Kind", "IsEnabled", "NextRunAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ScriptTriggers_ScriptId",
                table: "ScriptTriggers",
                column: "ScriptId");

            migrationBuilder.CreateIndex(
                name: "IX_ScriptVersions_ScriptId_VersionNumber",
                table: "ScriptVersions",
                columns: new[] { "ScriptId", "VersionNumber" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ScriptPublications");

            migrationBuilder.DropTable(
                name: "ScriptRuns");

            migrationBuilder.DropTable(
                name: "ScriptTriggers");

            migrationBuilder.DropTable(
                name: "ScriptVersions");

            migrationBuilder.DropTable(
                name: "Scripts");
        }
    }
}
