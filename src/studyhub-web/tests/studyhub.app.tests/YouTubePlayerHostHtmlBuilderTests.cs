using studyhub.app.services;
using Xunit;

namespace studyhub.app.tests;

public sealed class YouTubePlayerHostHtmlBuilderTests
{
    [Fact]
    public void BuildHostUrl_IncludesInitialStartOffset_WhenOffsetIsPositive()
    {
        var snapshot = new ExternalLessonPlaybackSnapshot
        {
            SessionToken = 7,
            CourseId = Guid.NewGuid(),
            LessonId = Guid.NewGuid(),
            VideoId = "abc123XYZ89",
            Provider = "YouTube",
            ExternalUrl = "https://www.youtube.com/watch?v=abc123XYZ89",
            RequestedPlaybackSpeed = 1.0,
            InitialStartOffset = TimeSpan.FromSeconds(10)
        };

        var url = YouTubePlayerHostHtmlBuilder.BuildHostUrl(snapshot);
        var uri = new Uri(url);
        var query = ParseQuery(uri.Query);

        Assert.True(query.TryGetValue("initialStartOffset", out var rawOffset));
        Assert.Equal("10", rawOffset);
    }

    [Fact]
    public void BuildHostUrl_OmitsInitialStartOffset_WhenOffsetIsZero()
    {
        var snapshot = new ExternalLessonPlaybackSnapshot
        {
            SessionToken = 9,
            CourseId = Guid.NewGuid(),
            LessonId = Guid.NewGuid(),
            VideoId = "xyz123abc45",
            Provider = "YouTube",
            ExternalUrl = "https://www.youtube.com/watch?v=xyz123abc45",
            RequestedPlaybackSpeed = 1.0,
            InitialStartOffset = TimeSpan.Zero
        };

        var url = YouTubePlayerHostHtmlBuilder.BuildHostUrl(snapshot);
        var uri = new Uri(url);
        var query = ParseQuery(uri.Query);

        Assert.False(query.ContainsKey("initialStartOffset"));
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
        {
            return values;
        }

        var pairs = query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries);
        foreach (var pair in pairs)
        {
            var separatorIndex = pair.IndexOf('=');
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = Uri.UnescapeDataString(pair[..separatorIndex]);
            var value = Uri.UnescapeDataString(pair[(separatorIndex + 1)..]);
            values[key] = value;
        }

        return values;
    }
}
