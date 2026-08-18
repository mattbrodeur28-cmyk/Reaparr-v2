namespace Reaparr.PlexApi.UnitTests;

/// <summary>
/// Pins the Plex JSON quirks that made deserializing into the SDK's models fail. Each case here
/// corresponds to an exception seen against a real server while syncing a music library.
/// </summary>
public class PlexJsonMediaItemParserUnitTests : BaseUnitTest
{
    [Test]
    public void ShouldParseTrack_WhenPlexSendsItsRealWorldQuirks()
    {
        // Arrange - optimizedForStreaming as 1, guid as a string, numeric fields as Plex sends them
        const string json = """
        {
          "MediaContainer": {
            "size": 1,
            "Metadata": [
              {
                "ratingKey": "12345",
                "key": "/library/metadata/12345",
                "type": "track",
                "title": "Nude",
                "parentTitle": "In Rainbows",
                "grandparentTitle": "Radiohead",
                "parentGuid": "plex://album/abc",
                "guid": "plex://track/def",
                "index": 3,
                "parentIndex": 1,
                "duration": 255000,
                "addedAt": 1600000000,
                "updatedAt": 1600000001,
                "Media": [
                  {
                    "id": 777,
                    "duration": 255000,
                    "bitrate": 1008,
                    "audioChannels": 2,
                    "audioCodec": "flac",
                    "container": "flac",
                    "optimizedForStreaming": 1,
                    "Part": [
                      {
                        "id": 888,
                        "key": "/library/parts/888/file.flac",
                        "duration": 255000,
                        "file": "/music/Radiohead/In Rainbows/03 Nude.flac",
                        "size": 41234567,
                        "container": "flac",
                        "Stream": [
                          {
                            "id": 999,
                            "streamType": 2,
                            "codec": "flac",
                            "bitrate": 1008,
                            "channels": 2,
                            "samplingRate": 44100,
                            "bitDepth": 16,
                            "selected": true
                          }
                        ]
                      }
                    ]
                  }
                ]
              }
            ]
          }
        }
        """;

        // Act
        var result = PlexJsonMediaItemParser.ParseMediaItems(json);

        // Assert
        result.Count.ShouldBe(1);

        var track = result[0];
        track.RatingKey.ShouldBe(12345);
        track.Title.ShouldBe("Nude");
        track.Type.ShouldBe(PlexMediaType.Song);
        track.Index.ShouldBe(3);
        track.ParentIndex.ShouldBe(1);
        track.Guid.ShouldBe("plex://track/def");

        // Duration is milliseconds on the wire and seconds on the DTO
        track.Duration.ShouldBe(255);

        var media = track.Media.ShouldHaveSingleItem();

        // 1 rather than true is what broke the SDK deserializer
        media.OptimizedForStreaming.ShouldBeTrue();
        media.AudioCodec.ShouldBe("flac");

        var part = media.Parts.ShouldHaveSingleItem();
        part.Size.ShouldBe(41234567);

        var stream = part.Stream.ShouldHaveSingleItem();
        stream.StreamType.ShouldBe(StreamType.Audio);
        stream.SamplingRate.ShouldBe(44100);
        stream.BitDepth.ShouldBe(16);
    }

    [Test]
    public void ShouldTakeTheFirstEntry_WhenPlexSendsGuidAsAnArray()
    {
        // Arrange - some items carry guid as an array of objects rather than a string
        const string json = """
        {
          "MediaContainer": {
            "Metadata": [
              {
                "ratingKey": "1",
                "title": "Some Artist",
                "type": "artist",
                "guid": [ { "id": "plex://artist/first" }, { "id": "plex://artist/second" } ]
              }
            ]
          }
        }
        """;

        // Act
        var result = PlexJsonMediaItemParser.ParseMediaItems(json);

        // Assert
        result.ShouldHaveSingleItem().Guid.ShouldBe("plex://artist/first");
    }

    [Test]
    public void ShouldReturnEmpty_WhenTheContainerCarriesNoMetadata()
    {
        // Arrange - the end of a paged listing looks like this, and must not be an error
        const string json = """{ "MediaContainer": { "size": 0 } }""";

        // Act
        var result = PlexJsonMediaItemParser.ParseMediaItems(json);

        // Assert
        result.ShouldBeEmpty();
    }

    [Test]
    public void ShouldNotThrow_WhenFieldsAreMissingOrUnexpectedlyTyped()
    {
        // Arrange - a deliberately hostile item: absent fields, quoted numbers, unknown extras
        const string json = """
        {
          "MediaContainer": {
            "Metadata": [
              {
                "ratingKey": "42",
                "title": "Sparse",
                "type": "track",
                "duration": "60000",
                "hasVoiceActivity": 0,
                "somethingReaparrDoesNotKnow": { "nested": true }
              }
            ]
          }
        }
        """;

        // Act
        var result = PlexJsonMediaItemParser.ParseMediaItems(json);

        // Assert
        var item = result.ShouldHaveSingleItem();
        item.RatingKey.ShouldBe(42);

        // A quoted number still parses
        item.Duration.ShouldBe(60);

        // Absent collections come back empty rather than null
        item.Media.ShouldBeEmpty();
        item.Genre.ShouldBeEmpty();
        item.Guid.ShouldBeEmpty();
    }
}
