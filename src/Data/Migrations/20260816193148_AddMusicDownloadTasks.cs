using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reaparr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddMusicDownloadTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DownloadTaskMusicArtist",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlexApiRatingKey = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false, collation: "NATURALSORT"),
                    DownloadStatus = table.Column<string>(type: "TEXT", unicode: false, maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FullTitle = table.Column<string>(type: "TEXT", nullable: false),
                    PlexServerId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlexLibraryId = table.Column<int>(type: "INTEGER", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadTaskMusicArtist", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DownloadTaskMusicArtist_PlexLibraries_PlexLibraryId",
                        column: x => x.PlexLibraryId,
                        principalTable: "PlexLibraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DownloadTaskMusicArtist_PlexServers_PlexServerId",
                        column: x => x.PlexServerId,
                        principalTable: "PlexServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DownloadTaskMusicAlbum",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlexApiRatingKey = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false, collation: "NATURALSORT"),
                    DownloadStatus = table.Column<string>(type: "TEXT", unicode: false, maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FullTitle = table.Column<string>(type: "TEXT", nullable: false),
                    PlexServerId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlexLibraryId = table.Column<int>(type: "INTEGER", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    ParentId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadTaskMusicAlbum", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DownloadTaskMusicAlbum_DownloadTaskMusicArtist_ParentId",
                        column: x => x.ParentId,
                        principalTable: "DownloadTaskMusicArtist",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DownloadTaskMusicAlbum_PlexLibraries_PlexLibraryId",
                        column: x => x.PlexLibraryId,
                        principalTable: "PlexLibraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DownloadTaskMusicAlbum_PlexServers_PlexServerId",
                        column: x => x.PlexServerId,
                        principalTable: "PlexServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DownloadTaskMusicTrack",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlexApiRatingKey = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false, collation: "NATURALSORT"),
                    DownloadStatus = table.Column<string>(type: "TEXT", unicode: false, maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FullTitle = table.Column<string>(type: "TEXT", nullable: false),
                    PlexServerId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlexLibraryId = table.Column<int>(type: "INTEGER", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    ParentId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadTaskMusicTrack", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DownloadTaskMusicTrack_DownloadTaskMusicAlbum_ParentId",
                        column: x => x.ParentId,
                        principalTable: "DownloadTaskMusicAlbum",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DownloadTaskMusicTrack_PlexLibraries_PlexLibraryId",
                        column: x => x.PlexLibraryId,
                        principalTable: "PlexLibraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DownloadTaskMusicTrack_PlexServers_PlexServerId",
                        column: x => x.PlexServerId,
                        principalTable: "PlexServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DownloadTaskMusicTrackFile",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlexApiRatingKey = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false, collation: "NATURALSORT"),
                    DownloadStatus = table.Column<string>(type: "TEXT", unicode: false, maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FullTitle = table.Column<string>(type: "TEXT", nullable: false),
                    PlexServerId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlexLibraryId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlexApiMediaId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlexApiPartId = table.Column<int>(type: "INTEGER", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false, collation: "NATURALSORT"),
                    FileLocationUrl = table.Column<string>(type: "TEXT", nullable: false),
                    HashId = table.Column<string>(type: "TEXT", nullable: true),
                    Quality = table.Column<int>(type: "INTEGER", nullable: false),
                    DirectoryMeta = table.Column<string>(type: "TEXT", nullable: false),
                    DataReceived = table.Column<long>(type: "INTEGER", nullable: false),
                    DataTotal = table.Column<long>(type: "INTEGER", nullable: false),
                    DownloadSpeed = table.Column<long>(type: "INTEGER", nullable: false),
                    DirectDownloadSnapshot = table.Column<string>(type: "TEXT", nullable: true),
                    DownloadClientType = table.Column<string>(type: "TEXT", unicode: false, maxLength: 10, nullable: false, defaultValue: "Direct"),
                    FileTransferSpeed = table.Column<long>(type: "INTEGER", nullable: false),
                    FileDataTransferred = table.Column<long>(type: "INTEGER", nullable: false),
                    CurrentFileTransferBytesOffset = table.Column<long>(type: "INTEGER", nullable: false),
                    Percentage = table.Column<decimal>(type: "TEXT", nullable: false),
                    TimeRemaining = table.Column<int>(type: "INTEGER", nullable: false),
                    DestinationFolderPathId = table.Column<int>(type: "INTEGER", nullable: true),
                    ParentId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadTaskMusicTrackFile", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DownloadTaskMusicTrackFile_DownloadTaskMusicTrack_ParentId",
                        column: x => x.ParentId,
                        principalTable: "DownloadTaskMusicTrack",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DownloadTaskMusicTrackFile_PlexLibraries_PlexLibraryId",
                        column: x => x.PlexLibraryId,
                        principalTable: "PlexLibraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DownloadTaskMusicTrackFile_PlexServers_PlexServerId",
                        column: x => x.PlexServerId,
                        principalTable: "PlexServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DownloadTaskMusicTrackFileLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", unicode: false, maxLength: 20, nullable: false, defaultValue: "Unknown"),
                    LogLevel = table.Column<string>(type: "TEXT", unicode: false, maxLength: 20, nullable: false, defaultValue: "None"),
                    Message = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DownloadTaskFileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DownloadTaskMusicTrackId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DownloadTaskMusicAlbumId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DownloadTaskMusicArtistId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DownloadTaskMusicTrackFileLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DownloadTaskMusicTrackFileLogs_DownloadTaskMusicAlbum_DownloadTaskMusicAlbumId",
                        column: x => x.DownloadTaskMusicAlbumId,
                        principalTable: "DownloadTaskMusicAlbum",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DownloadTaskMusicTrackFileLogs_DownloadTaskMusicArtist_DownloadTaskMusicArtistId",
                        column: x => x.DownloadTaskMusicArtistId,
                        principalTable: "DownloadTaskMusicArtist",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DownloadTaskMusicTrackFileLogs_DownloadTaskMusicTrackFile_DownloadTaskFileId",
                        column: x => x.DownloadTaskFileId,
                        principalTable: "DownloadTaskMusicTrackFile",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DownloadTaskMusicTrackFileLogs_DownloadTaskMusicTrack_DownloadTaskMusicTrackId",
                        column: x => x.DownloadTaskMusicTrackId,
                        principalTable: "DownloadTaskMusicTrack",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadTaskMusicAlbum_DownloadStatus",
                table: "DownloadTaskMusicAlbum",
                column: "DownloadStatus");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadTaskMusicAlbum_ParentId",
                table: "DownloadTaskMusicAlbum",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadTaskMusicAlbum_PlexLibraryId",
                table: "DownloadTaskMusicAlbum",
                column: "PlexLibraryId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadTaskMusicAlbum_PlexServerId",
                table: "DownloadTaskMusicAlbum",
                column: "PlexServerId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadTaskMusicArtist_DownloadStatus",
                table: "DownloadTaskMusicArtist",
                column: "DownloadStatus");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadTaskMusicArtist_PlexLibraryId",
                table: "DownloadTaskMusicArtist",
                column: "PlexLibraryId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadTaskMusicArtist_PlexServerId",
                table: "DownloadTaskMusicArtist",
                column: "PlexServerId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadTaskMusicArtist_PlexServerId_PlexApiRatingKey",
                table: "DownloadTaskMusicArtist",
                columns: new[] { "PlexServerId", "PlexApiRatingKey" });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadTaskMusicTrack_DownloadStatus",
                table: "DownloadTaskMusicTrack",
                column: "DownloadStatus");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadTaskMusicTrack_ParentId",
                table: "DownloadTaskMusicTrack",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadTaskMusicTrack_PlexLibraryId",
                table: "DownloadTaskMusicTrack",
                column: "PlexLibraryId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadTaskMusicTrack_PlexServerId",
                table: "DownloadTaskMusicTrack",
                column: "PlexServerId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadTaskMusicTrackFile_DownloadStatus",
                table: "DownloadTaskMusicTrackFile",
                column: "DownloadStatus");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadTaskMusicTrackFile_HashId",
                table: "DownloadTaskMusicTrackFile",
                column: "HashId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadTaskMusicTrackFile_ParentId",
                table: "DownloadTaskMusicTrackFile",
                column: "ParentId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadTaskMusicTrackFile_PlexLibraryId",
                table: "DownloadTaskMusicTrackFile",
                column: "PlexLibraryId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadTaskMusicTrackFile_PlexLibraryId_PlexServerId_PlexApiRatingKey",
                table: "DownloadTaskMusicTrackFile",
                columns: new[] { "PlexLibraryId", "PlexServerId", "PlexApiRatingKey" });

            migrationBuilder.CreateIndex(
                name: "IX_DownloadTaskMusicTrackFile_PlexServerId",
                table: "DownloadTaskMusicTrackFile",
                column: "PlexServerId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadTaskMusicTrackFileLogs_DownloadTaskFileId",
                table: "DownloadTaskMusicTrackFileLogs",
                column: "DownloadTaskFileId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadTaskMusicTrackFileLogs_DownloadTaskMusicAlbumId",
                table: "DownloadTaskMusicTrackFileLogs",
                column: "DownloadTaskMusicAlbumId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadTaskMusicTrackFileLogs_DownloadTaskMusicArtistId",
                table: "DownloadTaskMusicTrackFileLogs",
                column: "DownloadTaskMusicArtistId");

            migrationBuilder.CreateIndex(
                name: "IX_DownloadTaskMusicTrackFileLogs_DownloadTaskMusicTrackId",
                table: "DownloadTaskMusicTrackFileLogs",
                column: "DownloadTaskMusicTrackId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DownloadTaskMusicTrackFileLogs");

            migrationBuilder.DropTable(
                name: "DownloadTaskMusicTrackFile");

            migrationBuilder.DropTable(
                name: "DownloadTaskMusicTrack");

            migrationBuilder.DropTable(
                name: "DownloadTaskMusicAlbum");

            migrationBuilder.DropTable(
                name: "DownloadTaskMusicArtist");
        }
    }
}
