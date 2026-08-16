namespace Reaparr.Data.Configurations;

public class DownloadTaskMusicTrackFileConfiguration : IEntityTypeConfiguration<DownloadTaskMusicTrackFile>
{
    public void Configure(EntityTypeBuilder<DownloadTaskMusicTrackFile> builder)
    {
        builder.HasIndex(x => new
        {
            x.PlexLibraryId,
            x.PlexServerId,
            x.PlexApiRatingKey,
        });
        builder.HasIndex(x => x.HashId);

        builder
            .Property(b => b.DownloadStatus)
            .HasMaxLength(20)
            .HasConversion(x => x.ToDownloadStatusString(), x => x.ToDownloadStatus())
            .IsUnicode(false);

        builder.Property(c => c.FileName).UseCollation(OrderByNaturalExtensions.CollationName);
    }
}
