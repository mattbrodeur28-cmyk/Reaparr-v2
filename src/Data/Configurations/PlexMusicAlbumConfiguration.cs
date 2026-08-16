namespace Reaparr.Data.Configurations;

public class PlexMusicAlbumConfiguration : IEntityTypeConfiguration<PlexMusicAlbum>
{
    public void Configure(EntityTypeBuilder<PlexMusicAlbum> builder)
    {
        // See PlexMusicArtistConfiguration — VideoQuality has no meaning for music.
        builder.Ignore(x => x.Quality);

        builder.HasIndex(x => new { x.PlexLibraryId, x.SortIndex });
        builder.HasIndex(x => new { x.ArtistId, x.SortIndex });
        builder.HasIndex(x => x.SearchTitle);

        builder.HasIndex(x => new { x.PlexApiRatingKey, x.PlexServerId });

        builder
            .HasMany(x => x.Tracks)
            .WithOne(x => x.Album)
            .HasForeignKey(x => x.AlbumId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
