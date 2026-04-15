using System.Net;
using System.Text.Json;
using System.Xml;
using Microsoft.Extensions.Logging;
using studyhub.application.Contracts.CourseBuilding;
using studyhub.application.Contracts.Integrations;
using studyhub.application.Interfaces;
using studyhub.application.Interfaces.Providers;

namespace studyhub.infrastructure.services;

public sealed class YouTubeDiscoveryProvider(
    IIntegrationSettingsService integrationSettingsService,
    ILogger<YouTubeDiscoveryProvider> logger) : IYouTubeDiscoveryProvider
{
    private const int MaxPlaylistVideos = 30;
    private static readonly HttpClient HttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(90)
    };

    private readonly IIntegrationSettingsService _integrationSettingsService = integrationSettingsService;
    private readonly ILogger<YouTubeDiscoveryProvider> _logger = logger;

    public async Task<YouTubeCourseDiscoveryResponse> DiscoverCourseSourcesAsync(YouTubeCourseDiscoveryRequest request, CancellationToken cancellationToken = default)
    {
        var apiKey = await ResolveApiKeyAsync(null, cancellationToken);
        var queries = NormalizeQueries(request.Queries, request.Topic, request.Objective);
        var rawCandidates = new Dictionary<string, CandidateSeed>(StringComparer.OrdinalIgnoreCase);
        var intentProfile = BuildIntentProfile(request.Topic, request.Objective);

        foreach (var query in queries)
        {
            using var playlistDocument = await SendGetAsync(BuildPlaylistSearchUrl(query, request.RegionCode, request.MaxResultsPerQuery, apiKey), cancellationToken);
            foreach (var seed in ParseSearchCandidates(playlistDocument, query))
            {
                var key = $"{seed.SourceKind}:{seed.SourceId}";
                if (rawCandidates.TryGetValue(key, out var existing))
                {
                    foreach (var matchedQuery in seed.MatchedQueries)
                    {
                        existing.MatchedQueries.Add(matchedQuery);
                    }
                    continue;
                }

                rawCandidates[key] = seed;
            }

            using var videoDocument = await SendGetAsync(BuildVideoSearchUrl(query, request.RegionCode, request.MaxResultsPerQuery, apiKey), cancellationToken);
            foreach (var seed in ParseSearchCandidates(videoDocument, query))
            {
                var key = $"{seed.SourceKind}:{seed.SourceId}";
                if (rawCandidates.TryGetValue(key, out var existing))
                {
                    foreach (var matchedQuery in seed.MatchedQueries)
                    {
                        existing.MatchedQueries.Add(matchedQuery);
                    }
                    continue;
                }

                rawCandidates[key] = seed;
            }
        }

        var channelIds = rawCandidates.Values
            .Select(seed => seed.ChannelId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var channelStats = await LoadChannelStatsAsync(channelIds, apiKey, cancellationToken);

        var playlistSeeds = rawCandidates.Values
            .Where(seed => string.Equals(seed.SourceKind, "playlist", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var directVideoSeeds = rawCandidates.Values
            .Where(seed => string.Equals(seed.SourceKind, "video", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var playlistBundles = await BuildPlaylistBundlesAsync(playlistSeeds, channelStats, apiKey, cancellationToken);
        var directVideoDetails = await LoadVideoDescriptorsAsync(
            directVideoSeeds.Select(seed => seed.VideoId).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            apiKey,
            cancellationToken);

        var candidates = new List<YouTubeSourceCandidate>();

        foreach (var seed in playlistSeeds)
        {
            if (!playlistBundles.TryGetValue(seed.PlaylistId, out var bundle))
            {
                continue;
            }

            var subscriberCount = channelStats.TryGetValue(seed.ChannelId, out var stats)
                ? stats.SubscriberCount
                : 0L;

            candidates.Add(new YouTubeSourceCandidate
            {
                SourceKind = "playlist",
                SourceId = seed.SourceId,
                PlaylistId = seed.PlaylistId,
                Title = bundle.Title,
                Description = bundle.Description,
                Url = bundle.Url,
                ThumbnailUrl = bundle.ThumbnailUrl,
                ChannelId = seed.ChannelId,
                ChannelTitle = seed.ChannelTitle,
                ChannelUrl = seed.ChannelUrl,
                SubscriberCount = subscriberCount,
                ItemCount = bundle.Videos.Count,
                Duration = TimeSpan.FromTicks(bundle.Videos.Sum(video => video.Duration.Ticks)),
                AuthorityScore = ComputeAuthorityScore(subscriberCount, bundle.Videos.Count),
                RelevanceScore = ComputeRelevanceScore(seed.MatchedQueries, bundle.Title, bundle.Description, true, intentProfile),
                MatchedQuery = seed.MatchedQueries.FirstOrDefault() ?? string.Empty
            });
        }

        foreach (var seed in directVideoSeeds)
        {
            if (!directVideoDetails.TryGetValue(seed.VideoId, out var detail))
            {
                continue;
            }

            var subscriberCount = channelStats.TryGetValue(seed.ChannelId, out var stats)
                ? stats.SubscriberCount
                : 0L;

            candidates.Add(new YouTubeSourceCandidate
            {
                SourceKind = "video",
                SourceId = seed.SourceId,
                VideoId = detail.VideoId,
                Title = detail.Title,
                Description = detail.Description,
                Url = detail.Url,
                ThumbnailUrl = detail.ThumbnailUrl,
                ChannelId = detail.ChannelId,
                ChannelTitle = detail.ChannelTitle,
                ChannelUrl = seed.ChannelUrl,
                SubscriberCount = subscriberCount,
                ItemCount = 1,
                Duration = detail.Duration,
                AuthorityScore = ComputeAuthorityScore(subscriberCount, 1),
                RelevanceScore = ComputeRelevanceScore(seed.MatchedQueries, detail.Title, detail.Description, false, intentProfile),
                MatchedQuery = seed.MatchedQueries.FirstOrDefault() ?? string.Empty
            });
        }

        var orderedCandidates = candidates
            .OrderByDescending(candidate => candidate.RelevanceScore + candidate.AuthorityScore)
            .ThenByDescending(candidate => candidate.ItemCount)
            .ToList();

        return new YouTubeCourseDiscoveryResponse
        {
            Candidates = orderedCandidates,
            PlaylistBundles = playlistBundles.Values
                .OrderByDescending(bundle => bundle.SubscriberCount)
                .ToList()
        };
    }

    public async Task<YoutubeMaterialDiscoveryResponse> DiscoverMaterialsAsync(YoutubeMaterialDiscoveryRequest request, CancellationToken cancellationToken = default)
    {
        var apiKey = await ResolveApiKeyAsync(null, cancellationToken);
        var intentProfile = BuildIntentProfile(
            request.CourseTitle,
            string.Join(". ", request.ModuleTitles.Concat(request.LessonTitles).Concat(request.SelectedSources).Take(40)));
        var queries = NormalizeQueries(
                request.SeedQueries
                    .Concat(request.ModuleTitles)
                    .Concat(request.LessonTitles.Take(20))
                    .Concat(request.SelectedSources.Take(10)),
                request.CourseTitle,
                request.CourseDescription)
            .Take(8)
            .ToList();

        var seeds = new Dictionary<string, CandidateSeed>(StringComparer.OrdinalIgnoreCase);
        foreach (var query in queries)
        {
            using var playlistDocument = await SendGetAsync(BuildPlaylistSearchUrl(query, "US", 3, apiKey), cancellationToken);
            foreach (var seed in ParseSearchCandidates(playlistDocument, query))
            {
                var key = $"{seed.SourceKind}:{seed.SourceId}";
                if (seeds.TryGetValue(key, out var existing))
                {
                    foreach (var matchedQuery in seed.MatchedQueries)
                    {
                        existing.MatchedQueries.Add(matchedQuery);
                    }
                    continue;
                }

                seeds[key] = seed;
            }

            using var videoDocument = await SendGetAsync(BuildVideoSearchUrl(query, "US", 4, apiKey), cancellationToken);
            foreach (var seed in ParseSearchCandidates(videoDocument, query))
            {
                var key = $"{seed.SourceKind}:{seed.SourceId}";
                if (seeds.TryGetValue(key, out var existing))
                {
                    foreach (var matchedQuery in seed.MatchedQueries)
                    {
                        existing.MatchedQueries.Add(matchedQuery);
                    }
                    continue;
                }

                seeds[key] = seed;
            }
        }

        var channelIds = seeds.Values
            .Select(seed => seed.ChannelId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var channelStats = await LoadChannelStatsAsync(channelIds, apiKey, cancellationToken);
        var playlistSeeds = seeds.Values
            .Where(seed => string.Equals(seed.SourceKind, "playlist", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var directVideoSeeds = seeds.Values
            .Where(seed => string.Equals(seed.SourceKind, "video", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var playlistBundles = await BuildPlaylistBundlesAsync(playlistSeeds, channelStats, apiKey, cancellationToken);
        var videoDetails = await LoadVideoDescriptorsAsync(
            directVideoSeeds.Select(seed => seed.VideoId).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
            apiKey,
            cancellationToken);

        var candidates = new List<YoutubeMaterialCandidate>();

        foreach (var seed in playlistSeeds)
        {
            if (!playlistBundles.TryGetValue(seed.PlaylistId, out var bundle))
            {
                continue;
            }

            var subscriberCount = channelStats.TryGetValue(seed.ChannelId, out var stats)
                ? stats.SubscriberCount
                : 0L;

            candidates.Add(new YoutubeMaterialCandidate
            {
                VideoId = bundle.PlaylistId,
                Title = bundle.Title,
                ChannelName = bundle.ChannelTitle,
                IsPlaylist = true,
                AuthorityScore = ComputeAuthorityScore(subscriberCount, bundle.Videos.Count),
                RelevanceScore = ComputeRelevanceScore(seed.MatchedQueries, bundle.Title, bundle.Description, true, intentProfile)
            });
        }

        candidates.AddRange(directVideoSeeds
            .Where(seed => videoDetails.ContainsKey(seed.VideoId))
            .Select(seed =>
            {
                var detail = videoDetails[seed.VideoId];
                var subscriberCount = channelStats.TryGetValue(seed.ChannelId, out var stats)
                    ? stats.SubscriberCount
                    : 0L;

                return new YoutubeMaterialCandidate
                {
                    VideoId = detail.VideoId,
                    Title = detail.Title,
                    ChannelName = detail.ChannelTitle,
                    IsPlaylist = false,
                    AuthorityScore = ComputeAuthorityScore(subscriberCount, 1),
                    RelevanceScore = ComputeRelevanceScore(seed.MatchedQueries, detail.Title, detail.Description, false, intentProfile)
                };
            })
            .ToList());

        return new YoutubeMaterialDiscoveryResponse
        {
            Candidates = candidates
                .OrderByDescending(candidate => candidate.RelevanceScore + candidate.AuthorityScore)
                .ThenByDescending(candidate => candidate.IsPlaylist)
                .ToList()
        };
    }

    public async Task<ProviderValidationResponse> ValidateApiKeyAsync(ProviderValidationRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            var apiKey = await ResolveApiKeyAsync(request.ApiKey, cancellationToken);
            using var _ = await SendGetAsync(
                BuildAbsoluteUrl("videos", new Dictionary<string, string>
                {
                    ["part"] = "id",
                    ["id"] = "dQw4w9WgXcQ",
                    ["key"] = apiKey
                }),
                cancellationToken);

            return new ProviderValidationResponse
            {
                IsValid = true,
                Message = "Conexao com YouTube Data API validada com sucesso."
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "YouTube API key validation failed.");
            return new ProviderValidationResponse
            {
                IsValid = false,
                Message = $"Falha ao validar YouTube: {ex.Message}"
            };
        }
    }

    private async Task<string> ResolveApiKeyAsync(string? apiKeyOverride, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(apiKeyOverride))
        {
            return apiKeyOverride.Trim();
        }

        var settings = await _integrationSettingsService.GetSettingsAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(settings.YouTubeApiKey))
        {
            throw new InvalidOperationException("A chave do YouTube nao esta configurada.");
        }

        return settings.YouTubeApiKey.Trim();
    }

    private async Task<Dictionary<string, ChannelStat>> LoadChannelStatsAsync(IReadOnlyList<string> channelIds, string apiKey, CancellationToken cancellationToken)
    {
        if (channelIds.Count == 0)
        {
            return new Dictionary<string, ChannelStat>(StringComparer.OrdinalIgnoreCase);
        }

        using var document = await SendGetAsync(
            BuildAbsoluteUrl("channels", new Dictionary<string, string>
            {
                ["part"] = "snippet,statistics",
                ["id"] = string.Join(",", channelIds),
                ["key"] = apiKey
            }),
            cancellationToken);

        var result = new Dictionary<string, ChannelStat>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in document.RootElement.GetProperty("items").EnumerateArray())
        {
            var id = item.GetProperty("id").GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var snippet = item.GetProperty("snippet");
            var statistics = item.GetProperty("statistics");
            result[id] = new ChannelStat
            {
                Title = snippet.GetProperty("title").GetString() ?? string.Empty,
                CustomUrl = BuildChannelUrl(id),
                SubscriberCount = statistics.TryGetProperty("subscriberCount", out var subscribersNode) &&
                                  long.TryParse(subscribersNode.GetString(), out var subscribers)
                    ? subscribers
                    : 0L
            };
        }

        return result;
    }

    private async Task<Dictionary<string, YouTubePlaylistBundle>> BuildPlaylistBundlesAsync(
        IReadOnlyList<CandidateSeed> playlistSeeds,
        IReadOnlyDictionary<string, ChannelStat> channelStats,
        string apiKey,
        CancellationToken cancellationToken)
    {
        var playlistIds = playlistSeeds
            .Select(seed => seed.PlaylistId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (playlistIds.Count == 0)
        {
            return new Dictionary<string, YouTubePlaylistBundle>(StringComparer.OrdinalIgnoreCase);
        }

        using var playlistsDocument = await SendGetAsync(
            BuildAbsoluteUrl("playlists", new Dictionary<string, string>
            {
                ["part"] = "snippet,contentDetails",
                ["id"] = string.Join(",", playlistIds),
                ["key"] = apiKey
            }),
            cancellationToken);

        var bundles = new Dictionary<string, YouTubePlaylistBundle>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in playlistsDocument.RootElement.GetProperty("items").EnumerateArray())
        {
            var playlistId = item.GetProperty("id").GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(playlistId))
            {
                continue;
            }

            var snippet = item.GetProperty("snippet");
            var channelId = snippet.TryGetProperty("channelId", out var channelIdNode)
                ? channelIdNode.GetString() ?? string.Empty
                : string.Empty;
            var subscriberCount = channelStats.TryGetValue(channelId, out var stats)
                ? stats.SubscriberCount
                : 0L;

            bundles[playlistId] = new YouTubePlaylistBundle
            {
                PlaylistId = playlistId,
                Title = snippet.GetProperty("title").GetString() ?? string.Empty,
                Description = snippet.GetProperty("description").GetString() ?? string.Empty,
                Url = BuildPlaylistUrl(playlistId),
                ThumbnailUrl = ReadBestThumbnailUrl(snippet),
                ChannelId = channelId,
                ChannelTitle = snippet.TryGetProperty("channelTitle", out var channelTitleNode)
                    ? channelTitleNode.GetString() ?? string.Empty
                    : string.Empty,
                SubscriberCount = subscriberCount,
                Videos = []
            };
        }

        foreach (var playlistId in bundles.Keys.ToList())
        {
            var videos = await LoadPlaylistVideosAsync(playlistId, apiKey, cancellationToken);
            bundles[playlistId].Videos = videos;
        }

        return bundles;
    }

    private async Task<IReadOnlyList<YouTubeVideoDescriptor>> LoadPlaylistVideosAsync(string playlistId, string apiKey, CancellationToken cancellationToken)
    {
        using var playlistItemsDocument = await SendGetAsync(
            BuildAbsoluteUrl("playlistItems", new Dictionary<string, string>
            {
                ["part"] = "snippet,contentDetails",
                ["playlistId"] = playlistId,
                ["maxResults"] = MaxPlaylistVideos.ToString(),
                ["key"] = apiKey
            }),
            cancellationToken);

        var videoIds = new List<string>();
        var orderHints = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var seedData = new Dictionary<string, CandidateSeed>(StringComparer.OrdinalIgnoreCase);
        var order = 1;

        foreach (var item in playlistItemsDocument.RootElement.GetProperty("items").EnumerateArray())
        {
            if (!item.TryGetProperty("contentDetails", out var contentDetails) ||
                !contentDetails.TryGetProperty("videoId", out var videoIdNode))
            {
                continue;
            }

            var videoId = videoIdNode.GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(videoId))
            {
                continue;
            }

            videoIds.Add(videoId);
            orderHints[videoId] = order++;

            var snippet = item.GetProperty("snippet");
            seedData[videoId] = new CandidateSeed
            {
                SourceKind = "video",
                SourceId = videoId,
                VideoId = videoId,
                Title = snippet.TryGetProperty("title", out var titleNode) ? titleNode.GetString() ?? string.Empty : string.Empty,
                Description = snippet.TryGetProperty("description", out var descriptionNode) ? descriptionNode.GetString() ?? string.Empty : string.Empty,
                Url = BuildVideoUrl(videoId),
                ThumbnailUrl = ReadBestThumbnailUrl(snippet),
                ChannelId = snippet.TryGetProperty("channelId", out var channelIdNode) ? channelIdNode.GetString() ?? string.Empty : string.Empty,
                ChannelTitle = snippet.TryGetProperty("channelTitle", out var channelTitleNode) ? channelTitleNode.GetString() ?? string.Empty : string.Empty,
                ChannelUrl = snippet.TryGetProperty("channelId", out var channelForUrlNode) ? BuildChannelUrl(channelForUrlNode.GetString() ?? string.Empty) : string.Empty
            };
        }

        var details = await LoadVideoDescriptorsAsync(videoIds, apiKey, cancellationToken);

        return videoIds
            .Where(details.ContainsKey)
            .Select(videoId =>
            {
                var detail = details[videoId];
                var seed = seedData[videoId];
                detail.OrderHint = orderHints[videoId];
                detail.RelevanceScore = ComputeRelevanceScore([], detail.Title, detail.Description, false, new IntentProfile());
                detail.ThumbnailUrl = string.IsNullOrWhiteSpace(detail.ThumbnailUrl) ? seed.ThumbnailUrl : detail.ThumbnailUrl;
                return detail;
            })
            .OrderBy(item => item.OrderHint)
            .ToList();
    }

    private async Task<Dictionary<string, YouTubeVideoDescriptor>> LoadVideoDescriptorsAsync(
        IReadOnlyList<string> videoIds,
        string apiKey,
        CancellationToken cancellationToken)
    {
        if (videoIds.Count == 0)
        {
            return new Dictionary<string, YouTubeVideoDescriptor>(StringComparer.OrdinalIgnoreCase);
        }

        using var videosDocument = await SendGetAsync(
            BuildAbsoluteUrl("videos", new Dictionary<string, string>
            {
                ["part"] = "snippet,contentDetails",
                ["id"] = string.Join(",", videoIds),
                ["key"] = apiKey
            }),
            cancellationToken);

        var details = new Dictionary<string, YouTubeVideoDescriptor>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in videosDocument.RootElement.GetProperty("items").EnumerateArray())
        {
            var id = item.GetProperty("id").GetString() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(id))
            {
                continue;
            }

            var snippet = item.GetProperty("snippet");
            var contentDetails = item.GetProperty("contentDetails");
            details[id] = new YouTubeVideoDescriptor
            {
                VideoId = id,
                Title = snippet.GetProperty("title").GetString() ?? string.Empty,
                Description = snippet.GetProperty("description").GetString() ?? string.Empty,
                Url = BuildVideoUrl(id),
                ThumbnailUrl = ReadBestThumbnailUrl(snippet),
                ChannelId = snippet.TryGetProperty("channelId", out var channelIdNode) ? channelIdNode.GetString() ?? string.Empty : string.Empty,
                ChannelTitle = snippet.TryGetProperty("channelTitle", out var channelTitleNode) ? channelTitleNode.GetString() ?? string.Empty : string.Empty,
                Duration = ParseDuration(contentDetails.TryGetProperty("duration", out var durationNode)
                    ? durationNode.GetString()
                    : null)
            };
        }

        return details;
    }

    private static IEnumerable<CandidateSeed> ParseSearchCandidates(JsonDocument document, string query)
    {
        if (!document.RootElement.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array)
        {
            yield break;
        }

        foreach (var item in items.EnumerateArray())
        {
            if (!item.TryGetProperty("id", out var idNode) || !item.TryGetProperty("snippet", out var snippet))
            {
                continue;
            }

            var kind = idNode.TryGetProperty("kind", out var kindNode)
                ? kindNode.GetString() ?? string.Empty
                : string.Empty;

            CandidateSeed? seed = kind switch
            {
                "youtube#playlist" => new CandidateSeed
                {
                    SourceKind = "playlist",
                    SourceId = idNode.GetProperty("playlistId").GetString() ?? string.Empty,
                    PlaylistId = idNode.GetProperty("playlistId").GetString() ?? string.Empty
                },
                "youtube#video" => new CandidateSeed
                {
                    SourceKind = "video",
                    SourceId = idNode.GetProperty("videoId").GetString() ?? string.Empty,
                    VideoId = idNode.GetProperty("videoId").GetString() ?? string.Empty
                },
                _ => null
            };

            if (seed == null || string.IsNullOrWhiteSpace(seed.SourceId))
            {
                continue;
            }

            seed.Title = snippet.TryGetProperty("title", out var titleNode) ? titleNode.GetString() ?? string.Empty : string.Empty;
            seed.Description = snippet.TryGetProperty("description", out var descriptionNode) ? descriptionNode.GetString() ?? string.Empty : string.Empty;
            seed.ChannelId = snippet.TryGetProperty("channelId", out var channelIdNode) ? channelIdNode.GetString() ?? string.Empty : string.Empty;
            seed.ChannelTitle = snippet.TryGetProperty("channelTitle", out var channelTitleNode) ? channelTitleNode.GetString() ?? string.Empty : string.Empty;
            seed.ChannelUrl = BuildChannelUrl(seed.ChannelId);
            seed.ThumbnailUrl = ReadBestThumbnailUrl(snippet);
            seed.Url = seed.SourceKind == "playlist"
                ? BuildPlaylistUrl(seed.PlaylistId)
                : BuildVideoUrl(seed.VideoId);
            seed.MatchedQueries.Add(query);

            yield return seed;
        }
    }

    private async Task<JsonDocument> SendGetAsync(string absoluteUrl, CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync(absoluteUrl, cancellationToken);
        var rawBody = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(BuildYouTubeErrorMessage(rawBody, response.StatusCode));
        }

        return JsonDocument.Parse(rawBody);
    }

    private static string BuildSearchUrl(
        string query,
        string regionCode,
        int maxResults,
        string apiKey,
        string sourceKind,
        IReadOnlyDictionary<string, string>? extraParameters = null)
    {
        var parameters = new Dictionary<string, string>
        {
            ["part"] = "snippet",
            ["maxResults"] = Math.Max(1, maxResults).ToString(),
            ["q"] = query,
            ["type"] = sourceKind,
            ["regionCode"] = string.IsNullOrWhiteSpace(regionCode) ? "US" : regionCode,
            ["key"] = apiKey
        };

        if (extraParameters != null)
        {
            foreach (var parameter in extraParameters)
            {
                parameters[parameter.Key] = parameter.Value;
            }
        }

        return BuildAbsoluteUrl("search", parameters);
    }

    private static string BuildAbsoluteUrl(string resource, IReadOnlyDictionary<string, string> query)
    {
        var parts = query
            .Where(pair => !string.IsNullOrWhiteSpace(pair.Value))
            .Select(pair => $"{Uri.EscapeDataString(pair.Key)}={Uri.EscapeDataString(pair.Value)}");

        return $"https://www.googleapis.com/youtube/v3/{resource}?{string.Join("&", parts)}";
    }

    private static List<string> NormalizeQueries(IEnumerable<string> seedQueries, string titleSeed, string objectiveSeed)
    {
        var queries = seedQueries
            .Where(query => !string.IsNullOrWhiteSpace(query))
            .Select(query => query.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (!string.IsNullOrWhiteSpace(titleSeed))
        {
            queries.Add(titleSeed.Trim());
        }

        if (!string.IsNullOrWhiteSpace(titleSeed) && !string.IsNullOrWhiteSpace(objectiveSeed))
        {
            queries.Add($"{titleSeed.Trim()} {objectiveSeed.Trim()}");
        }

        return queries
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(query => query.Length >= 3)
            .ToList();
    }

    private static double ComputeAuthorityScore(long subscriberCount, int itemCount)
    {
        var normalizedSubscribers = Math.Clamp(Math.Log10(Math.Max(10, subscriberCount)) / 6d, 0d, 1d);
        var sequenceBonus = Math.Clamp(itemCount / 12d, 0d, 1d);
        return Math.Round((normalizedSubscribers * 80d) + (sequenceBonus * 20d), 2);
    }

    private static double ComputeRelevanceScore(
        IReadOnlyCollection<string> matchedQueries,
        string title,
        string description,
        bool isPlaylist,
        IntentProfile intentProfile)
    {
        var tokens = matchedQueries
            .SelectMany(query => query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Where(token => token.Length >= 3)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (tokens.Count == 0)
        {
            return isPlaylist ? 60d : 50d;
        }

        var lowerTitle = title.ToLowerInvariant();
        var lowerDescription = description.ToLowerInvariant();
        var titleHits = tokens.Count(token => lowerTitle.Contains(token.ToLowerInvariant(), StringComparison.Ordinal));
        var descriptionHits = tokens.Count(token => lowerDescription.Contains(token.ToLowerInvariant(), StringComparison.Ordinal));
        var playlistBonus = isPlaylist ? 10d : 0d;
        var beginnerBonus = intentProfile.IsBeginnerJourney && LooksBeginnerFriendly(lowerTitle, lowerDescription) ? 10d : 0d;
        var completeCourseBonus = isPlaylist && LooksLikeStructuredCourse(lowerTitle, lowerDescription) ? 12d : 0d;

        return Math.Round(Math.Clamp(25d + (titleHits * 15d) + (descriptionHits * 5d) + playlistBonus + beginnerBonus + completeCourseBonus, 0d, 100d), 2);
    }

    private static string BuildPlaylistSearchUrl(string query, string regionCode, int maxResults, string apiKey)
    {
        return BuildSearchUrl(query, regionCode, maxResults, apiKey, "playlist");
    }

    private static string BuildVideoSearchUrl(string query, string regionCode, int maxResults, string apiKey)
    {
        return BuildSearchUrl(
            query,
            regionCode,
            maxResults,
            apiKey,
            "video",
            new Dictionary<string, string>
            {
                ["videoEmbeddable"] = "true",
                ["videoSyndicated"] = "true"
            });
    }

    private static IntentProfile BuildIntentProfile(string titleSeed, string objectiveSeed)
    {
        var combined = $"{titleSeed} {objectiveSeed}".ToLowerInvariant();
        var isBeginnerJourney =
            combined.Contains("do zero", StringComparison.Ordinal) ||
            combined.Contains("iniciante", StringComparison.Ordinal) ||
            combined.Contains("basico", StringComparison.Ordinal) ||
            combined.Contains("básico", StringComparison.Ordinal) ||
            combined.Contains("from scratch", StringComparison.Ordinal) ||
            combined.Contains("fundamentos", StringComparison.Ordinal);

        return new IntentProfile
        {
            IsBeginnerJourney = isBeginnerJourney
        };
    }

    private static bool LooksBeginnerFriendly(string lowerTitle, string lowerDescription)
    {
        return ContainsAny(lowerTitle, lowerDescription, "do zero", "iniciante", "basico", "básico", "fundamentos", "introducao", "introdução");
    }

    private static bool LooksLikeStructuredCourse(string lowerTitle, string lowerDescription)
    {
        return ContainsAny(lowerTitle, lowerDescription, "curso", "playlist", "modulo", "módulo", "completo", "aulas");
    }

    private static bool ContainsAny(string lowerTitle, string lowerDescription, params string[] tokens)
    {
        foreach (var token in tokens)
        {
            if (lowerTitle.Contains(token, StringComparison.Ordinal) ||
                lowerDescription.Contains(token, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static TimeSpan ParseDuration(string? rawDuration)
    {
        if (string.IsNullOrWhiteSpace(rawDuration))
        {
            return TimeSpan.Zero;
        }

        try
        {
            return XmlConvert.ToTimeSpan(rawDuration);
        }
        catch
        {
            return TimeSpan.Zero;
        }
    }

    private static string ReadBestThumbnailUrl(JsonElement snippet)
    {
        if (!snippet.TryGetProperty("thumbnails", out var thumbnails))
        {
            return string.Empty;
        }

        foreach (var key in new[] { "maxres", "standard", "high", "medium", "default" })
        {
            if (thumbnails.TryGetProperty(key, out var node) &&
                node.TryGetProperty("url", out var urlNode))
            {
                return urlNode.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static string BuildVideoUrl(string videoId)
        => $"https://www.youtube.com/watch?v={videoId}";

    private static string BuildPlaylistUrl(string playlistId)
        => $"https://www.youtube.com/playlist?list={playlistId}";

    private static string BuildChannelUrl(string channelId)
        => string.IsNullOrWhiteSpace(channelId) ? string.Empty : $"https://www.youtube.com/channel/{channelId}";

    private static string BuildYouTubeErrorMessage(string rawBody, HttpStatusCode statusCode)
    {
        try
        {
            using var document = JsonDocument.Parse(rawBody);
            var message = document.RootElement
                .GetProperty("error")
                .GetProperty("message")
                .GetString();

            return string.IsNullOrWhiteSpace(message)
                ? $"YouTube retornou erro HTTP {(int)statusCode}."
                : message;
        }
        catch
        {
            return $"YouTube retornou erro HTTP {(int)statusCode}.";
        }
    }

    private sealed class CandidateSeed
    {
        public string SourceKind { get; set; } = string.Empty;
        public string SourceId { get; set; } = string.Empty;
        public string PlaylistId { get; set; } = string.Empty;
        public string VideoId { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string ThumbnailUrl { get; set; } = string.Empty;
        public string ChannelId { get; set; } = string.Empty;
        public string ChannelTitle { get; set; } = string.Empty;
        public string ChannelUrl { get; set; } = string.Empty;
        public List<string> MatchedQueries { get; } = [];
    }

    private sealed class ChannelStat
    {
        public string Title { get; set; } = string.Empty;
        public string CustomUrl { get; set; } = string.Empty;
        public long SubscriberCount { get; set; }
    }

    private sealed class IntentProfile
    {
        public bool IsBeginnerJourney { get; set; }
    }
}
