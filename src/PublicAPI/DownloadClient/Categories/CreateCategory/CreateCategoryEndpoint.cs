namespace Reaparr.PublicAPI.CreateCategory;

public record CreateCategoryRequest
{
    [BindFrom("category")]
    public required string Category { get; init; }

    /// <summary>
    /// Accepted for qBittorrent API compatibility and deliberately not stored.
    /// </summary>
    /// <remarks>
    /// Reaparr downloads into its own configured download folder, so echoing a client-supplied
    /// path back from torrents/categories would misreport where the files actually are and can
    /// throw off remote path mapping. torrents/categories always reports the real download folder.
    /// </remarks>
    [BindFrom("savePath")]
    public string? SavePath { get; init; }
}

public class CreateCategoryEndpoint : Endpoint<CreateCategoryRequest>
{
    private readonly ILogger _log;
    private readonly IReaparrDbContext _dbContext;

    public CreateCategoryEndpoint(ILogger logger, IReaparrDbContext dbContext)
    {
        _log = logger.ForContext<CreateCategoryEndpoint>();
        _dbContext = dbContext;
    }

    public override void Configure()
    {
        Post(PublicApiRoutes.DownloadClient + "/torrents/createCategory");
        Description(x => x.IsDownloadClient());
        AllowFormData(urlEncoded: true);
        AllowAnonymous();
        PreProcessor<DownloadClientAuthenticationPreProcessor<CreateCategoryRequest>>();
    }

    public override async Task HandleAsync(CreateCategoryRequest req, CancellationToken ct)
    {
        _log.Here().DebugApiCall(HttpContext, req);

        var name = req.Category?.Trim();

        // qBittorrent answers 400 to an empty category name.
        if (string.IsNullOrWhiteSpace(name))
        {
            await Send.ResponseAsync("Category name is empty", StatusCodes.Status400BadRequest, ct);
            return;
        }

        // Built-in categories are always advertised, so there is nothing to persist for them.
        if (IntegrationDefinitions.IsBuiltInCategory(name))
        {
            await Send.OkAsync(cancellation: ct);
            return;
        }

        var exists = await _dbContext.DownloadClientCategories.AnyAsync(x => x.Name == name, ct);
        if (!exists)
        {
            // The arr lists the categories again straight after this call and fails its
            // download-client validation when the name it just created is still missing.
            _dbContext.DownloadClientCategories.Add(
                new DownloadClientCategory { Name = name, CreatedAt = DateTime.UtcNow }
            );
            await _dbContext.SaveChangesAsync(ct);

            _log.Here().Information("Created download client category {CategoryName}", name);
        }

        await Send.OkAsync(cancellation: ct);
    }
}
