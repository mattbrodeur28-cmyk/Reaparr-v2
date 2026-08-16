using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reaparr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlexMusicMediaAndAudioQuality : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AlbumCount",
                table: "PlexLibraries",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0)
                .Annotation("Relational:ColumnOrder", 23);

            migrationBuilder.AddColumn<int>(
                name: "ArtistCount",
                table: "PlexLibraries",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0)
                .Annotation("Relational:ColumnOrder", 22);

            migrationBuilder.AddColumn<int>(
                name: "TrackCount",
                table: "PlexLibraries",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0)
                .Annotation("Relational:ColumnOrder", 24);

            migrationBuilder.CreateTable(
                name: "PlexMusicArtists",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlexApiRatingKey = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    SortIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    SearchTitle = table.Column<string>(type: "TEXT", nullable: false),
                    Duration = table.Column<int>(type: "INTEGER", nullable: false),
                    MediaSize = table.Column<long>(type: "INTEGER", nullable: false),
                    PlexApiMetaDataKey = table.Column<int>(type: "INTEGER", nullable: false),
                    Studio = table.Column<string>(type: "TEXT", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: false),
                    ContentRating = table.Column<string>(type: "TEXT", nullable: false),
                    Rating = table.Column<double>(type: "REAL", nullable: false),
                    ChildCount = table.Column<int>(type: "INTEGER", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    OriginallyAvailableAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    HasThumb = table.Column<bool>(type: "INTEGER", nullable: false),
                    HasArt = table.Column<bool>(type: "INTEGER", nullable: false),
                    HasTheme = table.Column<bool>(type: "INTEGER", nullable: false),
                    FullTitle = table.Column<string>(type: "TEXT", nullable: false),
                    Guid = table.Column<string>(type: "TEXT", nullable: false),
                    Guid_IMDB = table.Column<string>(type: "TEXT", nullable: true),
                    Guid_TMDB = table.Column<int>(type: "INTEGER", nullable: true),
                    Guid_TVDB = table.Column<int>(type: "INTEGER", nullable: true),
                    GrandChildCount = table.Column<int>(type: "INTEGER", nullable: false),
                    Guid_MusicBrainz = table.Column<string>(type: "TEXT", nullable: true),
                    PlexLibraryId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlexServerId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlexMusicArtists", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlexMusicArtists_PlexLibraries_PlexLibraryId",
                        column: x => x.PlexLibraryId,
                        principalTable: "PlexLibraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlexMusicArtists_PlexServers_PlexServerId",
                        column: x => x.PlexServerId,
                        principalTable: "PlexServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlexMusicAlbums",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlexApiRatingKey = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    SortIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    SearchTitle = table.Column<string>(type: "TEXT", nullable: false),
                    Duration = table.Column<int>(type: "INTEGER", nullable: false),
                    MediaSize = table.Column<long>(type: "INTEGER", nullable: false),
                    PlexApiMetaDataKey = table.Column<int>(type: "INTEGER", nullable: false),
                    Studio = table.Column<string>(type: "TEXT", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: false),
                    ContentRating = table.Column<string>(type: "TEXT", nullable: false),
                    Rating = table.Column<double>(type: "REAL", nullable: false),
                    ChildCount = table.Column<int>(type: "INTEGER", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    OriginallyAvailableAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    HasThumb = table.Column<bool>(type: "INTEGER", nullable: false),
                    HasArt = table.Column<bool>(type: "INTEGER", nullable: false),
                    HasTheme = table.Column<bool>(type: "INTEGER", nullable: false),
                    FullTitle = table.Column<string>(type: "TEXT", nullable: false),
                    Guid = table.Column<string>(type: "TEXT", nullable: false),
                    Guid_IMDB = table.Column<string>(type: "TEXT", nullable: true),
                    Guid_TMDB = table.Column<int>(type: "INTEGER", nullable: true),
                    Guid_TVDB = table.Column<int>(type: "INTEGER", nullable: true),
                    ParentKey = table.Column<int>(type: "INTEGER", nullable: false),
                    ParentGuid = table.Column<string>(type: "TEXT", nullable: true),
                    Guid_MusicBrainz = table.Column<string>(type: "TEXT", nullable: true),
                    ArtistId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlexLibraryId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlexServerId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlexMusicAlbums", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlexMusicAlbums_PlexLibraries_PlexLibraryId",
                        column: x => x.PlexLibraryId,
                        principalTable: "PlexLibraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlexMusicAlbums_PlexMusicArtists_ArtistId",
                        column: x => x.ArtistId,
                        principalTable: "PlexMusicArtists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlexMusicAlbums_PlexServers_PlexServerId",
                        column: x => x.PlexServerId,
                        principalTable: "PlexServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlexMusicTracks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlexApiRatingKey = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    SortIndex = table.Column<int>(type: "INTEGER", nullable: false),
                    SearchTitle = table.Column<string>(type: "TEXT", nullable: false),
                    Duration = table.Column<int>(type: "INTEGER", nullable: false),
                    MediaSize = table.Column<long>(type: "INTEGER", nullable: false),
                    PlexApiMetaDataKey = table.Column<int>(type: "INTEGER", nullable: false),
                    Studio = table.Column<string>(type: "TEXT", nullable: false),
                    Summary = table.Column<string>(type: "TEXT", nullable: false),
                    ContentRating = table.Column<string>(type: "TEXT", nullable: false),
                    Rating = table.Column<double>(type: "REAL", nullable: false),
                    ChildCount = table.Column<int>(type: "INTEGER", nullable: false),
                    AddedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    OriginallyAvailableAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    HasThumb = table.Column<bool>(type: "INTEGER", nullable: false),
                    HasArt = table.Column<bool>(type: "INTEGER", nullable: false),
                    HasTheme = table.Column<bool>(type: "INTEGER", nullable: false),
                    FullTitle = table.Column<string>(type: "TEXT", nullable: false),
                    Guid = table.Column<string>(type: "TEXT", nullable: false),
                    Guid_IMDB = table.Column<string>(type: "TEXT", nullable: true),
                    Guid_TMDB = table.Column<int>(type: "INTEGER", nullable: true),
                    Guid_TVDB = table.Column<int>(type: "INTEGER", nullable: true),
                    TrackNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    DiscNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    ParentKey = table.Column<int>(type: "INTEGER", nullable: false),
                    ParentGuid = table.Column<string>(type: "TEXT", nullable: true),
                    Guid_MusicBrainz = table.Column<string>(type: "TEXT", nullable: true),
                    ArtistId = table.Column<int>(type: "INTEGER", nullable: false),
                    AlbumId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlexLibraryId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlexServerId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlexMusicTracks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlexMusicTracks_PlexLibraries_PlexLibraryId",
                        column: x => x.PlexLibraryId,
                        principalTable: "PlexLibraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlexMusicTracks_PlexMusicAlbums_AlbumId",
                        column: x => x.AlbumId,
                        principalTable: "PlexMusicAlbums",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlexMusicTracks_PlexMusicArtists_ArtistId",
                        column: x => x.ArtistId,
                        principalTable: "PlexMusicArtists",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlexMusicTracks_PlexServers_PlexServerId",
                        column: x => x.PlexServerId,
                        principalTable: "PlexServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlexMusicTrackData",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlexApiRatingKey = table.Column<int>(type: "INTEGER", nullable: false),
                    PlexApiMediaId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlexApiPartId = table.Column<int>(type: "INTEGER", nullable: false),
                    VideoResolution = table.Column<int>(type: "INTEGER", nullable: false),
                    OriginalFilename = table.Column<string>(type: "TEXT", nullable: false),
                    GeneratedFilename = table.Column<string>(type: "TEXT", nullable: true),
                    Container = table.Column<string>(type: "TEXT", nullable: false),
                    Duration = table.Column<int>(type: "INTEGER", nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    VideoCodec = table.Column<string>(type: "TEXT", nullable: false),
                    AudioCodec = table.Column<string>(type: "TEXT", nullable: false),
                    NeedsGeneratedName = table.Column<bool>(type: "INTEGER", nullable: false),
                    GeneratedNameSyncedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PlexMusicTrackId = table.Column<int>(type: "INTEGER", nullable: false),
                    AudioQuality = table.Column<int>(type: "INTEGER", nullable: false),
                    Bitrate = table.Column<int>(type: "INTEGER", nullable: true),
                    SampleRate = table.Column<int>(type: "INTEGER", nullable: true),
                    BitDepth = table.Column<int>(type: "INTEGER", nullable: true),
                    Channels = table.Column<int>(type: "INTEGER", nullable: true),
                    PlexLibraryId = table.Column<int>(type: "INTEGER", nullable: false),
                    PlexServerId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlexMusicTrackData", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlexMusicTrackData_PlexLibraries_PlexLibraryId",
                        column: x => x.PlexLibraryId,
                        principalTable: "PlexLibraries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlexMusicTrackData_PlexMusicTracks_PlexMusicTrackId",
                        column: x => x.PlexMusicTrackId,
                        principalTable: "PlexMusicTracks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_PlexMusicTrackData_PlexServers_PlexServerId",
                        column: x => x.PlexServerId,
                        principalTable: "PlexServers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PlexMusicAlbums_ArtistId_SortIndex",
                table: "PlexMusicAlbums",
                columns: new[] { "ArtistId", "SortIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_PlexMusicAlbums_PlexApiRatingKey_PlexServerId",
                table: "PlexMusicAlbums",
                columns: new[] { "PlexApiRatingKey", "PlexServerId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlexMusicAlbums_PlexLibraryId_SortIndex",
                table: "PlexMusicAlbums",
                columns: new[] { "PlexLibraryId", "SortIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_PlexMusicAlbums_PlexServerId",
                table: "PlexMusicAlbums",
                column: "PlexServerId");

            migrationBuilder.CreateIndex(
                name: "IX_PlexMusicAlbums_SearchTitle",
                table: "PlexMusicAlbums",
                column: "SearchTitle");

            migrationBuilder.CreateIndex(
                name: "IX_PlexMusicArtists_PlexApiRatingKey_PlexServerId",
                table: "PlexMusicArtists",
                columns: new[] { "PlexApiRatingKey", "PlexServerId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlexMusicArtists_PlexLibraryId_SortIndex",
                table: "PlexMusicArtists",
                columns: new[] { "PlexLibraryId", "SortIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_PlexMusicArtists_PlexServerId",
                table: "PlexMusicArtists",
                column: "PlexServerId");

            migrationBuilder.CreateIndex(
                name: "IX_PlexMusicArtists_SearchTitle",
                table: "PlexMusicArtists",
                column: "SearchTitle");

            migrationBuilder.CreateIndex(
                name: "IX_PlexMusicArtists_SortIndex",
                table: "PlexMusicArtists",
                column: "SortIndex");

            migrationBuilder.CreateIndex(
                name: "IX_PlexMusicTrackData_AudioQuality",
                table: "PlexMusicTrackData",
                column: "AudioQuality");

            migrationBuilder.CreateIndex(
                name: "IX_PlexMusicTrackData_PlexApiPartId",
                table: "PlexMusicTrackData",
                column: "PlexApiPartId");

            migrationBuilder.CreateIndex(
                name: "IX_PlexMusicTrackData_PlexApiRatingKey",
                table: "PlexMusicTrackData",
                column: "PlexApiRatingKey");

            migrationBuilder.CreateIndex(
                name: "IX_PlexMusicTrackData_PlexLibraryId",
                table: "PlexMusicTrackData",
                column: "PlexLibraryId");

            migrationBuilder.CreateIndex(
                name: "IX_PlexMusicTrackData_PlexMusicTrackId_AudioQuality",
                table: "PlexMusicTrackData",
                columns: new[] { "PlexMusicTrackId", "AudioQuality" });

            migrationBuilder.CreateIndex(
                name: "IX_PlexMusicTrackData_PlexServerId",
                table: "PlexMusicTrackData",
                column: "PlexServerId");

            migrationBuilder.CreateIndex(
                name: "IX_PlexMusicTracks_AlbumId_SortIndex",
                table: "PlexMusicTracks",
                columns: new[] { "AlbumId", "SortIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_PlexMusicTracks_ArtistId_SortIndex",
                table: "PlexMusicTracks",
                columns: new[] { "ArtistId", "SortIndex" });

            migrationBuilder.CreateIndex(
                name: "IX_PlexMusicTracks_PlexApiRatingKey_PlexServerId",
                table: "PlexMusicTracks",
                columns: new[] { "PlexApiRatingKey", "PlexServerId" });

            migrationBuilder.CreateIndex(
                name: "IX_PlexMusicTracks_PlexLibraryId",
                table: "PlexMusicTracks",
                column: "PlexLibraryId");

            migrationBuilder.CreateIndex(
                name: "IX_PlexMusicTracks_PlexServerId",
                table: "PlexMusicTracks",
                column: "PlexServerId");

            migrationBuilder.CreateIndex(
                name: "IX_PlexMusicTracks_SearchTitle",
                table: "PlexMusicTracks",
                column: "SearchTitle");

            migrationBuilder.CreateIndex(
                name: "IX_PlexMusicTracks_SortIndex",
                table: "PlexMusicTracks",
                column: "SortIndex");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlexMusicTrackData");

            migrationBuilder.DropTable(
                name: "PlexMusicTracks");

            migrationBuilder.DropTable(
                name: "PlexMusicAlbums");

            migrationBuilder.DropTable(
                name: "PlexMusicArtists");

            migrationBuilder.DropColumn(
                name: "AlbumCount",
                table: "PlexLibraries");

            migrationBuilder.DropColumn(
                name: "ArtistCount",
                table: "PlexLibraries");

            migrationBuilder.DropColumn(
                name: "TrackCount",
                table: "PlexLibraries");
        }
    }
}
