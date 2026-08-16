namespace Reaparr.Data.Configurations;

public class PlexMusicArtistConfiguration : IEntityTypeConfiguration<PlexMusicArtist>
{
    public void Configure(EntityTypeBuilder<PlexMusicArtist> builder)
    {
        // Quality on the base entity is a VideoQuality and has no meaning for music.
        // Audio quality lives on PlexMusicTrackMediaData.AudioQuality instead.
        builder.Ignore(x => x.Quality);

        builder.HasIndex(x => x.SortIndex);
        builder.HasIndex(x => new { x.PlexLibraryId, x.SortIndex });
        builder.HasIndex(x => x.SearchTitle);

        builder.HasIndex(x => new { x.PlexApiRatingKey, x.PlexServerId });

        builder
            .HasMany(x => x.Albums)
            .WithOne(x => x.Artist)
            .HasForeignKey(x => x.ArtistId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
