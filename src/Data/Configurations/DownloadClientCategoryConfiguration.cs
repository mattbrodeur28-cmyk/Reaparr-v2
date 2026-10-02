namespace Reaparr.Data.Configurations;

public class DownloadClientCategoryConfiguration : IEntityTypeConfiguration<DownloadClientCategory>
{
    public void Configure(EntityTypeBuilder<DownloadClientCategory> builder)
    {
        // Category names are the lookup key used by torrents/categories and torrents/info.
        builder.HasIndex(x => x.Name).IsUnique();
    }
}
