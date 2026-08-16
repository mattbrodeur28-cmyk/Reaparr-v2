namespace Reaparr.Data.Configurations;

public class PlexMusicTrackConfiguration : IEntityTypeConfiguration<PlexMusicTrack>
{
    public void Configure(EntityTypeBuilder<PlexMusicTrack> builder)
    {
        // See PlexMusicArtistConfiguration — VideoQuality has no meaning for music.
        builder.Ignore(x => x.Quality);

        builder.HasIndex(x => x.SortIndex);
        builder.HasIndex(x => new { x.AlbumId, x.SortIndex });
        builder.HasIndex(x => new { x.ArtistId, x.SortIndex });

        // Backs the /music/search filters, which narrow on track title and then join up to
        // album and artist. Without this the endpoint table-scans every track in every library.
        builder.HasIndex(x => x.SearchTitle);

        builder.HasIndex(x => new { x.PlexApiRatingKey, x.PlexServerId });

        builder
            .HasMany(x => x.MediaDataList)
            .WithOne(x => x.PlexMusicTrack)
            .HasForeignKey(x => x.PlexMusicTrackId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
