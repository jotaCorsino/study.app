namespace studyhub.app.services;

internal static class YouTubePlayerHostHtmlBuilder
{
    public const string VirtualHostName = "studyhub-player.local";
    public const string RelativeHostPagePath = "external-player/youtube-host.html";
    public const string VirtualHostOrigin = "https://studyhub-player.local";
    private const string BlankPageUrl = "about:blank";

    public static string BuildHostUrl(ExternalLessonPlaybackSnapshot snapshot)
    {
        var query = new Dictionary<string, string?>
        {
            ["session"] = snapshot.SessionToken.ToString(),
            ["videoId"] = snapshot.VideoId,
            ["courseId"] = snapshot.CourseId?.ToString("D"),
            ["lessonId"] = snapshot.LessonId?.ToString("D"),
            ["externalUrl"] = snapshot.ExternalUrl,
            ["provider"] = snapshot.Provider,
            ["initialRate"] = snapshot.RequestedPlaybackSpeed.ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        return $"{VirtualHostOrigin}/{RelativeHostPagePath}?{BuildQueryString(query)}";
    }

    public static string BuildBlankUrl()
        => BlankPageUrl;

    private static string BuildQueryString(IReadOnlyDictionary<string, string?> values)
    {
        return string.Join(
            "&",
            values
                .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
                .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value!)}"));
    }
}
