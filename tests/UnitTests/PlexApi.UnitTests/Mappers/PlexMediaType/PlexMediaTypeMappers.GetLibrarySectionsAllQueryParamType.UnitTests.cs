using LukeHagar.PlexAPI.SDK.Models.Components;
using Reaparr.PlexApi.Contracts;

namespace Reaparr.PlexApi.UnitTests;

public class PlexMediaTypeMappersToPlexApiMediaTypeUnitTests : BaseUnitTest
{
    [Test]
    [Arguments(PlexMediaType.Movie, MediaType.Movie)]
    [Arguments(PlexMediaType.TvShow, MediaType.TvShow)]
    [Arguments(PlexMediaType.Season, MediaType.Season)]
    [Arguments(PlexMediaType.Episode, MediaType.Episode)]
    [Arguments(PlexMediaType.PhotoAlbum, MediaType.PhotoAlbum)]
    [Arguments(PlexMediaType.Photos, MediaType.Photo)]
    public void ShouldMapPlexMediaTypeToMediaType(PlexMediaType input, MediaType expected)
    {
        // Act
        input.ToPlexApiMediaType().ShouldBe(expected);
    }

    /// <summary>
    /// Music must serialize to Plex's own section type ids, which the SDK's sequential enum does
    /// not match: the SDK calls artist 5, Plex calls it 8. Plex answers 400 to type=5 on a music
    /// section, so asserting the numeric wire value is the only thing that protects this.
    /// Movie/show/season/episode are unaffected because their ids happen to coincide.
    /// </summary>
    [Test]
    [Arguments(PlexMediaType.Artist, 8)]
    [Arguments(PlexMediaType.Album, 9)]
    [Arguments(PlexMediaType.Song, 10)]
    public void ShouldMapMusicToPlexSectionTypeId(PlexMediaType input, int expectedPlexTypeId)
    {
        // Act
        var result = input.ToPlexApiMediaType();

        // Assert
        ((int)result).ShouldBe(expectedPlexTypeId);
    }
}
