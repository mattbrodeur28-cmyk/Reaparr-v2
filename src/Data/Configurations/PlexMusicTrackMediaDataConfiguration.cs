namespace Reaparr.Data.Configurations;

public class PlexMusicTrackMediaDataConfiguration : IEntityTypeConfiguration<PlexMusicTrackMediaData>
{
    public void Configure(EntityTypeBuilder<PlexMusicTrackMediaData> builder)
    {
        // The inherited Quality is a VideoQuality. Music ranks on AudioQuality instead, so the
        // indexes PlexTvShowEpisodeMediaData puts on Quality go on AudioQuality here.
        builder.Ignore(x => x.Quality);

        builder.HasIndex(x => x.AudioQuality);
        builder.HasIndex(x => new { x.PlexMusicTrackId, x.AudioQuality });
        builder.HasIndex(x => x.PlexApiRatingKey);

        // /torrents/download resolves a part by (PartId, PlexApiPartId) before generating
        // the .torrent, so that lookup gets its own index.
        builder.HasIndex(x => x.PlexApiPartId);

        builder
            .HasOne(x => x.PlexMusicTrack)
            .WithMany(x => x.MediaDataList)
            .HasForeignKey(x => x.PlexMusicTrackId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
