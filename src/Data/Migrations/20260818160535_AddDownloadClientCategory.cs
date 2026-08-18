using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Reaparr.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDownloadClientCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DownloadClientCategory",
                table: "DownloadTaskTvShowEpisodeFile",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DownloadClientCategory",
                table: "DownloadTaskMusicTrackFile",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DownloadClientCategory",
                table: "DownloadTaskMovieFile",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DownloadClientCategory",
                table: "DownloadTaskTvShowEpisodeFile");

            migrationBuilder.DropColumn(
                name: "DownloadClientCategory",
                table: "DownloadTaskMusicTrackFile");

            migrationBuilder.DropColumn(
                name: "DownloadClientCategory",
                table: "DownloadTaskMovieFile");
        }
    }
}
