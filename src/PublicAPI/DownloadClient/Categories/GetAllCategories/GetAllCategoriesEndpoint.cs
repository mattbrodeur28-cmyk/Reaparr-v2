namespace Reaparr.PublicAPI.GetAllCategories;

public class GetAllCategoriesEndpoint : EndpointWithoutRequest<object>
{
    private readonly ILogger _log;
    private readonly IReaparrDbContext _dbContext;

    public GetAllCategoriesEndpoint(ILogger logger, IReaparrDbContext dbContext)
    {
        _log = logger.ForContext<GetAllCategoriesEndpoint>();
        _dbContext = dbContext;
    }

    public override void Configure()
    {
        Get(PublicApiRoutes.DownloadClient + "/torrents/categories");
        Description(x => x.IsDownloadClient());
        AllowAnonymous();
        PreProcessor<DownloadClientAuthenticationPreProcessor<EmptyRequest>>();
    }

    public override async Task HandleAsync(CancellationToken ct)
    {
        _log.Here().DebugApiCall(HttpContext);

        var downloadFolder = await _dbContext.GetDownloadFolder();

        var categories = new Dictionary<string, object>
        {
            // This is the default category and avoids having to implement and track custom categories for Sonarr
            [IntegrationDefinitions.SONARR_DEFAULT_CATEGORY] = new
            {
                name = IntegrationDefinitions.SONARR_DEFAULT_CATEGORY,
                savePath = downloadFolder.DirectoryPath,
            },
            [IntegrationDefinitions.RADARR_DEFAULT_CATEGORY] = new
            {
                name = IntegrationDefinitions.RADARR_DEFAULT_CATEGORY,
                savePath = downloadFolder.DirectoryPath,
            },
            // Lidarr, and any other client polling /torrents/info by the music category.
            // Every arr validates its configured category by listing the labels here and, when the
            // label is absent, calling torrents/createCategory and listing again. createCategory is
            // a no-op, so a category missing from this list fails validation with
            // "Configuration of label failed" no matter how many times it is retried.
            [IntegrationDefinitions.MUSIC_DEFAULT_CATEGORY] = new
            {
                name = IntegrationDefinitions.MUSIC_DEFAULT_CATEGORY,
                savePath = downloadFolder.DirectoryPath,
            },
        };

        await Send.OkAsync(categories, cancellation: ct);
    }
}
