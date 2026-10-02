using Reaparr.PublicAPI.Contracts;
using Reaparr.PublicAPI.CreateCategory;

namespace Reaparr.PublicAPI.UnitTests;

public class CreateCategoryEndpointUnitTests : BaseEndpointUnitTest<CreateCategoryEndpoint, CreateCategoryRequest>
{
    [Test]
    public async Task ShouldPersistCategory_WhenNameIsNew()
    {
        // Arrange
        await SetupDatabase(7301, config => config.PlexServerCount = 1);

        // Act
        await TestEndpointHandleAsync(new CreateCategoryRequest { Category = "lidarr-custom" });

        // Assert
        var persisted = await IDbContext.DownloadClientCategories.ToListAsync(CancellationToken);
        persisted.Count.ShouldBe(1);
        persisted[0].Name.ShouldBe("lidarr-custom");
    }

    [Test]
    public async Task ShouldTrimCategory_WhenNameHasSurroundingWhitespace()
    {
        // Arrange
        await SetupDatabase(7302, config => config.PlexServerCount = 1);

        // Act
        await TestEndpointHandleAsync(new CreateCategoryRequest { Category = "  spaced-label  " });

        // Assert
        var persisted = await IDbContext.DownloadClientCategories.ToListAsync(CancellationToken);
        persisted.Count.ShouldBe(1);
        persisted[0].Name.ShouldBe("spaced-label");
    }

    [Test]
    public async Task ShouldNotPersistDuplicate_WhenCategoryAlreadyExists()
    {
        // Arrange - the unique index on Name would throw if a duplicate were inserted.
        await SetupDatabase(7303, config => config.PlexServerCount = 1);

        // Act
        await TestEndpointHandleAsync(new CreateCategoryRequest { Category = "repeat-label" });
        await TestEndpointHandleAsync(new CreateCategoryRequest { Category = "repeat-label" });

        // Assert
        var persisted = await IDbContext.DownloadClientCategories.ToListAsync(CancellationToken);
        persisted.Count.ShouldBe(1);
    }

    [Test]
    public async Task ShouldNotPersistAnything_WhenCategoryIsBuiltIn()
    {
        // Arrange - built-ins are always advertised, so storing them would be dead rows.
        await SetupDatabase(7304, config => config.PlexServerCount = 1);

        // Act
        await TestEndpointHandleAsync(
            new CreateCategoryRequest { Category = IntegrationDefinitions.MUSIC_DEFAULT_CATEGORY }
        );

        // Assert
        var persisted = await IDbContext.DownloadClientCategories.ToListAsync(CancellationToken);
        persisted.ShouldBeEmpty();
    }

    [Test]
    public async Task ShouldNotPersistAnything_WhenCategoryIsEmpty()
    {
        // Arrange
        await SetupDatabase(7305, config => config.PlexServerCount = 1);

        // Act
        await TestEndpointHandleAsync(new CreateCategoryRequest { Category = "   " });

        // Assert
        var persisted = await IDbContext.DownloadClientCategories.ToListAsync(CancellationToken);
        persisted.ShouldBeEmpty();
    }
}
