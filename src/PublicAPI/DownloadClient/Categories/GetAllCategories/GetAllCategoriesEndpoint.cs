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

        // Every arr validates its configured category by listing the labels here and, when the
        // label is absent, calling torrents/createCategory and listing again. A category missing
        // from this response therefore fails that validation permanently.
        var categories = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        // The built-in categories are always advertised so the integrations work on a database
        // that has never seen a createCategory call.
        foreach (var name in IntegrationDefinitions.BuiltInCategories)
            categories[name] = BuildCategory(name, downloadFolder.DirectoryPath);

        // Anything an arr created for itself, which is what allows a category name other than
        // Reaparr's defaults.
        var persisted = await _dbContext.DownloadClientCategories.AsNoTracking().Select(x => x.Name).ToListAsync(ct);

        foreach (var name in persisted)
            categories[name] = BuildCategory(name, downloadFolder.DirectoryPath);

        await Send.OkAsync(categories, cancellation: ct);
    }

    /// <summary>
    /// savePath always reports Reaparr's own download folder: that is where the files actually
    /// land, whatever path the client asked for when it created the category.
    /// </summary>
    private static object BuildCategory(string name, string savePath) => new { name, savePath };
}
