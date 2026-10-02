using System.Text.Json;
using Reaparr.PublicAPI.Contracts;
using Reaparr.PublicAPI.GetAllCategories;

namespace Reaparr.PublicAPI.UnitTests;

public class GetAllCategoriesEndpointUnitTests : BaseEndpointWithoutRequestUnitTest<GetAllCategoriesEndpoint, object>
{
    /// <summary>
    /// Every category an integration is configured with has to appear here.
    /// </summary>
    /// <remarks>
    /// Each arr validates its configured category by listing the labels from this endpoint and,
    /// when the label is absent, calling torrents/createCategory and listing again.
    /// CreateCategoryEndpoint is a no-op, so a category missing from this response fails the arr's
    /// download-client validation with "Configuration of label failed" and no amount of retrying
    /// helps. Lidarr shipped broken for exactly this reason: musicCategory was set to
    /// MUSIC_DEFAULT_CATEGORY while this endpoint only advertised the Sonarr and Radarr categories.
    /// </remarks>
    [Test]
    public async Task ShouldAdvertiseEveryIntegrationCategory_WhenCategoriesAreRequested()
    {
        // Arrange
        await SetupDatabase(7201, config => config.PlexServerCount = 1);

        // Act
        var result = await TestEndpointHandleAsync();

        // Assert
        result.Response.ShouldNotBeNull();

        var advertised = JsonSerializer
            .SerializeToDocument(result.Response)
            .RootElement.EnumerateObject()
            .Select(x => x.Name)
            .ToList();

        advertised.ShouldContain(IntegrationDefinitions.SONARR_DEFAULT_CATEGORY);
        advertised.ShouldContain(IntegrationDefinitions.RADARR_DEFAULT_CATEGORY);
        advertised.ShouldContain(IntegrationDefinitions.MUSIC_DEFAULT_CATEGORY);
    }
}
