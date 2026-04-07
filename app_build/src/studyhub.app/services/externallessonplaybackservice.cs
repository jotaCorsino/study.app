using System.Text.Json;
using Microsoft.Extensions.Logging;
using studyhub.application.Contracts.CourseBuilding;
using studyhub.application.Interfaces;
using studyhub.domain.Entities;

namespace studyhub.app.services;

public sealed class ExternalLessonPlaybackService(
    IProgressService progressService,
    ICourseGenerationHistoryService courseGenerationHistoryService,
    IExternalLessonRuntimeStateService externalLessonRuntimeStateService,
    ILogger<ExternalLessonPlaybackService> logger)
{
    private readonly IProgressService _progressService = progressService;
    private readonly ICourseGenerationHistoryService _courseGenerationHistoryService = courseGenerationHistoryService;
    private readonly IExternalLessonRuntimeStateService _externalLessonRuntimeStateService = externalLessonRuntimeStateService;
    private readonly ILogger<ExternalLessonPlaybackService> _logger = logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private ExternalLessonPlaybackSnapshot _snapshot = ExternalLessonPlaybackSnapshot.Hidden;
    private long _nextSessionToken;
    private int _lastPersistedSecond = -1;
    private bool _completionRaised;

    public event Action<ExternalLessonPlaybackSnapshot>? StateChanged;
    public event EventHandler<ExternalLessonPlaybackCompletedEventArgs>? LessonCompleted;

    public ExternalLessonPlaybackSnapshot Snapshot => _snapshot;
    public bool SupportsReliableResumePosition => false;

    public async Task<long> ActivateAsync(Guid courseId, Lesson? lesson)
    {
        ExternalLessonPlaybackSnapshot updatedSnapshot;

        await _gate.WaitAsync();
        try
        {
            var sessionToken = Interlocked.Increment(ref _nextSessionToken);
            _completionRaised = false;
            _lastPersistedSecond = lesson == null
                ? -1
                : (int)Math.Round(Math.Max(0, lesson.LastPlaybackPosition.TotalSeconds));

            updatedSnapshot = BuildActivationSnapshot(sessionToken, courseId, lesson);
            _snapshot = updatedSnapshot;
        }
        finally
        {
            _gate.Release();
        }

        await PersistActivationRuntimeStateAsync(updatedSnapshot, lesson);
        _logger.LogInformation(
            "External lesson playback activated. CourseId: {CourseId}. LessonId: {LessonId}. Provider: {Provider}. Status: {Status}. Url: {ExternalUrl}",
            courseId,
            updatedSnapshot.LessonId,
            updatedSnapshot.Provider,
            updatedSnapshot.Status,
            updatedSnapshot.ExternalUrl);
        RaiseStateChanged(updatedSnapshot);
        return updatedSnapshot.SessionToken;
    }

    public async Task UpdateViewportAsync(long sessionToken, ExternalLessonPlaybackViewport viewport)
    {
        ExternalLessonPlaybackSnapshot? changedSnapshot = null;

        await _gate.WaitAsync();
        try
        {
            if (_snapshot.SessionToken != sessionToken)
            {
                return;
            }

            var normalizedViewport = viewport.Normalize();
            if (_snapshot.Viewport == normalizedViewport)
            {
                return;
            }

            changedSnapshot = _snapshot with { Viewport = normalizedViewport };
            _snapshot = changedSnapshot;
        }
        finally
        {
            _gate.Release();
        }

        if (changedSnapshot != null)
        {
            RaiseStateChanged(changedSnapshot);
        }
    }

    public async Task HandlePlayerReadyAsync(long sessionToken, TimeSpan duration)
    {
        ExternalLessonPlaybackSnapshot snapshotAfterReady;

        await _gate.WaitAsync();
        try
        {
            if (_snapshot.SessionToken != sessionToken)
            {
                return;
            }

            snapshotAfterReady = _snapshot with
            {
                KnownDuration = duration > TimeSpan.Zero ? duration : _snapshot.KnownDuration,
                Status = ExternalLessonPlaybackStatus.Ready,
                StatusTitle = "Aula externa pronta",
                StatusMessage = "A aula do YouTube foi carregada no host externo do StudyHub."
            };

            _snapshot = snapshotAfterReady;
        }
        finally
        {
            _gate.Release();
        }

        await PersistReadyRuntimeStateAsync(snapshotAfterReady);
        RaiseStateChanged(snapshotAfterReady);
    }

    public async Task HandlePlayerStateChangedAsync(long sessionToken, YouTubeIframePlayerState state)
    {
        ExternalLessonPlaybackSnapshot? changedSnapshot = null;

        await _gate.WaitAsync();
        try
        {
            if (_snapshot.SessionToken != sessionToken)
            {
                return;
            }

            var nextSnapshot = state switch
            {
                YouTubeIframePlayerState.Unstarted => _snapshot with
                {
                    Status = ExternalLessonPlaybackStatus.Pending,
                    StatusTitle = "Abrindo aula externa",
                    StatusMessage = "O host externo do StudyHub esta preparando o video do YouTube."
                },
                YouTubeIframePlayerState.Buffering => _snapshot with
                {
                    Status = ExternalLessonPlaybackStatus.Pending,
                    StatusTitle = "Carregando aula externa",
                    StatusMessage = "O video do YouTube esta bufferizando no host externo."
                },
                YouTubeIframePlayerState.Playing => _snapshot with
                {
                    Status = ExternalLessonPlaybackStatus.Playing,
                    StatusTitle = "Reproducao iniciada",
                    StatusMessage = "A aula externa esta rodando no host do StudyHub."
                },
                YouTubeIframePlayerState.Paused => _snapshot with
                {
                    Status = ExternalLessonPlaybackStatus.Ready,
                    StatusTitle = "Aula pausada",
                    StatusMessage = "A aula externa continua pronta no host do StudyHub."
                },
                YouTubeIframePlayerState.Cued => _snapshot with
                {
                    Status = ExternalLessonPlaybackStatus.Ready,
                    StatusTitle = "Aula pronta",
                    StatusMessage = "A aula externa esta pronta para reproducao."
                },
                _ => _snapshot
            };

            if (nextSnapshot != _snapshot)
            {
                changedSnapshot = nextSnapshot;
                _snapshot = nextSnapshot;
            }
        }
        finally
        {
            _gate.Release();
        }

        if (changedSnapshot != null)
        {
            RaiseStateChanged(changedSnapshot);
        }
    }

    public async Task HandleProgressHeartbeatAsync(long sessionToken, TimeSpan position, TimeSpan duration)
    {
        ExternalLessonPlaybackSnapshot snapshotForPersistence;
        bool shouldPersist;
        TimeSpan resolvedPosition;

        await _gate.WaitAsync();
        try
        {
            if (_snapshot.SessionToken != sessionToken ||
                _snapshot.CourseId == null ||
                _snapshot.LessonId == null)
            {
                return;
            }

            var resolvedDuration = duration > TimeSpan.Zero ? duration : _snapshot.KnownDuration;
            resolvedPosition = position < TimeSpan.Zero ? TimeSpan.Zero : position;
            if (resolvedDuration > TimeSpan.Zero && resolvedPosition > resolvedDuration)
            {
                resolvedPosition = resolvedDuration;
            }

            var roundedSecond = (int)Math.Round(Math.Max(0, resolvedPosition.TotalSeconds));
            shouldPersist = roundedSecond > 0 && ShouldPersistSecond(roundedSecond);
            if (shouldPersist)
            {
                _lastPersistedSecond = roundedSecond;
            }

            snapshotForPersistence = _snapshot with
            {
                KnownDuration = resolvedDuration
            };

            _snapshot = snapshotForPersistence;
        }
        finally
        {
            _gate.Release();
        }

        if (!shouldPersist)
        {
            return;
        }

        await _progressService.UpdateLessonPlaybackAsync(
            snapshotForPersistence.CourseId!.Value,
            snapshotForPersistence.LessonId!.Value,
            resolvedPosition,
            snapshotForPersistence.KnownDuration);
    }

    public async Task HandlePlaybackEndedAsync(long sessionToken, TimeSpan duration)
    {
        ExternalLessonPlaybackSnapshot snapshotAfterCompletion;
        ExternalLessonPlaybackCompletedEventArgs? completionArgs = null;

        await _gate.WaitAsync();
        try
        {
            if (_snapshot.SessionToken != sessionToken ||
                _snapshot.CourseId == null ||
                _snapshot.LessonId == null)
            {
                return;
            }

            snapshotAfterCompletion = _snapshot with
            {
                KnownDuration = duration > TimeSpan.Zero ? duration : _snapshot.KnownDuration,
                Status = ExternalLessonPlaybackStatus.Ready,
                StatusTitle = "Aula concluida",
                StatusMessage = "A aula externa terminou e o progresso foi salvo pelo StudyHub."
            };

            _snapshot = snapshotAfterCompletion;
            _lastPersistedSecond = (int)Math.Round(Math.Max(0, snapshotAfterCompletion.KnownDuration.TotalSeconds));

            if (!_completionRaised)
            {
                _completionRaised = true;
                completionArgs = new ExternalLessonPlaybackCompletedEventArgs(
                    snapshotAfterCompletion.SessionToken,
                    snapshotAfterCompletion.CourseId.Value,
                    snapshotAfterCompletion.LessonId.Value);
            }
        }
        finally
        {
            _gate.Release();
        }

        await _progressService.MarkLessonCompletedAsync(
            snapshotAfterCompletion.CourseId!.Value,
            snapshotAfterCompletion.LessonId!.Value);

        RaiseStateChanged(snapshotAfterCompletion);
        if (completionArgs != null)
        {
            LessonCompleted?.Invoke(this, completionArgs);
        }
    }

    public async Task HandleEmbedFailedAsync(long sessionToken, string errorCode, string message, bool fallbackLaunched)
    {
        ExternalLessonPlaybackSnapshot? changedSnapshot = null;

        await _gate.WaitAsync();
        try
        {
            if (_snapshot.SessionToken != sessionToken)
            {
                return;
            }

            changedSnapshot = _snapshot with
            {
                Status = ExternalLessonPlaybackStatus.Error,
                StatusTitle = "Falha no player externo",
                StatusMessage = string.IsNullOrWhiteSpace(message)
                    ? "O host externo nao conseguiu reproduzir esta aula do YouTube."
                    : message
            };

            _snapshot = changedSnapshot;
        }
        finally
        {
            _gate.Release();
        }

        _logger.LogWarning(
            "External lesson playback failed. SessionToken: {SessionToken}. Provider: {Provider}. Url: {ExternalUrl}. ErrorCode: {ErrorCode}. Reason: {Reason}. FallbackLaunched: {FallbackLaunched}",
            sessionToken,
            changedSnapshot?.Provider,
            changedSnapshot?.ExternalUrl,
            errorCode,
            message,
            fallbackLaunched);

        if (changedSnapshot != null)
        {
            await PersistFailureRuntimeStateAsync(changedSnapshot, errorCode, message, fallbackLaunched);
            RaiseStateChanged(changedSnapshot);
        }
    }

    public async Task ReportBridgeFailureAsync(long sessionToken, string title, string message)
    {
        ExternalLessonPlaybackSnapshot? changedSnapshot = null;

        await _gate.WaitAsync();
        try
        {
            if (_snapshot.SessionToken != sessionToken)
            {
                return;
            }

            changedSnapshot = _snapshot with
            {
                Status = ExternalLessonPlaybackStatus.Error,
                StatusTitle = title,
                StatusMessage = message
            };

            _snapshot = changedSnapshot;
        }
        finally
        {
            _gate.Release();
        }

        if (changedSnapshot != null)
        {
            await PersistFailureRuntimeStateAsync(changedSnapshot, "bridge-error", message, fallbackLaunched: false);
            RaiseStateChanged(changedSnapshot);
        }
    }

    public async Task DeactivateAsync(long sessionToken)
    {
        ExternalLessonPlaybackSnapshot changedSnapshot;

        await _gate.WaitAsync();
        try
        {
            if (_snapshot.SessionToken != sessionToken)
            {
                return;
            }

            _completionRaised = false;
            _lastPersistedSecond = -1;
            changedSnapshot = ExternalLessonPlaybackSnapshot.Hidden;
            _snapshot = changedSnapshot;
        }
        finally
        {
            _gate.Release();
        }

        _logger.LogInformation(
            "External lesson playback deactivated. SessionToken: {SessionToken}",
            sessionToken);

        RaiseStateChanged(changedSnapshot);
    }

    private ExternalLessonPlaybackSnapshot BuildActivationSnapshot(long sessionToken, Guid courseId, Lesson? lesson)
    {
        if (lesson == null)
        {
            return ExternalLessonPlaybackSnapshot.Hidden with
            {
                SessionToken = sessionToken,
                CourseId = courseId,
                Status = ExternalLessonPlaybackStatus.Error,
                StatusTitle = "Aula indisponivel",
                StatusMessage = "Nenhuma fonte externa foi associada a esta aula."
            };
        }

        if (lesson.SourceType != LessonSourceType.ExternalVideo)
        {
            return ExternalLessonPlaybackSnapshot.Hidden with
            {
                SessionToken = sessionToken,
                CourseId = courseId,
                LessonId = lesson.Id,
                Status = ExternalLessonPlaybackStatus.Error,
                StatusTitle = "Fonte de aula invalida",
                StatusMessage = "A aula selecionada nao pertence ao runtime externo."
            };
        }

        if (!TryResolveYouTubeVideo(lesson, out var videoId, out var canonicalUrl))
        {
            return ExternalLessonPlaybackSnapshot.Hidden with
            {
                SessionToken = sessionToken,
                CourseId = courseId,
                LessonId = lesson.Id,
                Provider = lesson.Provider,
                ExternalUrl = lesson.ExternalUrl,
                Status = ExternalLessonPlaybackStatus.Error,
                StatusTitle = "Fonte externa nao suportada",
                StatusMessage = "O StudyHub ainda nao suporta a reproducao embutida desta fonte externa."
            };
        }

        return ExternalLessonPlaybackSnapshot.Hidden with
        {
            SessionToken = sessionToken,
            CourseId = courseId,
            LessonId = lesson.Id,
            Provider = "YouTube",
            ExternalUrl = canonicalUrl,
            VideoId = videoId,
            KnownDuration = lesson.Duration,
            Status = ExternalLessonPlaybackStatus.Pending,
            StatusTitle = "Abrindo aula externa",
            StatusMessage = "Carregando o video do YouTube no host externo do StudyHub."
        };
    }

    private bool ShouldPersistSecond(int roundedSecond)
    {
        if (roundedSecond <= 0)
        {
            return false;
        }

        if (_lastPersistedSecond < 0)
        {
            return true;
        }

        return Math.Abs(roundedSecond - _lastPersistedSecond) >= 5;
    }

    private async Task PersistActivationRuntimeStateAsync(ExternalLessonPlaybackSnapshot snapshot, Lesson? lesson)
    {
        if (snapshot.CourseId is not Guid courseId || lesson == null || lesson.Id == Guid.Empty)
        {
            return;
        }

        if (snapshot.Status == ExternalLessonPlaybackStatus.Error)
        {
            await PersistFailureRuntimeStateAsync(
                snapshot,
                "unsupported-source",
                snapshot.StatusMessage,
                fallbackLaunched: false);
            return;
        }

        await _externalLessonRuntimeStateService.RecordOpenedAsync(
            courseId,
            lesson.Id,
            FirstNonEmpty(snapshot.Provider, lesson.Provider),
            FirstNonEmpty(snapshot.ExternalUrl, lesson.ExternalUrl));

        await _courseGenerationHistoryService.RecordStepAsync(new CourseGenerationStepEntry
        {
            CourseId = courseId,
            StepKey = OnlineCourseStepKeys.ExternalPlayback,
            Provider = FirstNonEmpty(snapshot.Provider, lesson.Provider, "YouTube"),
            Status = CourseGenerationStepStatus.Running,
            RequestJson = SerializePayload(new
            {
                lessonId = lesson.Id,
                provider = FirstNonEmpty(snapshot.Provider, lesson.Provider),
                externalUrl = FirstNonEmpty(snapshot.ExternalUrl, lesson.ExternalUrl),
                sessionToken = snapshot.SessionToken
            })
        });
    }

    private async Task PersistReadyRuntimeStateAsync(ExternalLessonPlaybackSnapshot snapshot)
    {
        if (snapshot.CourseId is not Guid courseId || snapshot.LessonId is not Guid lessonId)
        {
            return;
        }

        await _externalLessonRuntimeStateService.RecordReadyAsync(
            courseId,
            lessonId,
            snapshot.Provider,
            snapshot.ExternalUrl);

        await _courseGenerationHistoryService.RecordStepAsync(new CourseGenerationStepEntry
        {
            CourseId = courseId,
            StepKey = OnlineCourseStepKeys.ExternalPlayback,
            Provider = FirstNonEmpty(snapshot.Provider, "YouTube"),
            Status = CourseGenerationStepStatus.Succeeded,
            RequestJson = SerializePayload(new
            {
                lessonId,
                provider = snapshot.Provider,
                externalUrl = snapshot.ExternalUrl,
                sessionToken = snapshot.SessionToken
            }),
            ResponseJson = SerializePayload(new
            {
                status = snapshot.Status.ToString(),
                knownDurationSeconds = Math.Round(snapshot.KnownDuration.TotalSeconds)
            })
        });
    }

    private async Task PersistFailureRuntimeStateAsync(
        ExternalLessonPlaybackSnapshot snapshot,
        string errorCode,
        string errorMessage,
        bool fallbackLaunched)
    {
        if (snapshot.CourseId is not Guid courseId || snapshot.LessonId is not Guid lessonId)
        {
            return;
        }

        await _externalLessonRuntimeStateService.RecordFailureAsync(
            courseId,
            lessonId,
            FirstNonEmpty(snapshot.Provider, "YouTube"),
            snapshot.ExternalUrl,
            errorCode,
            errorMessage,
            fallbackLaunched);

        await _courseGenerationHistoryService.RecordStepAsync(new CourseGenerationStepEntry
        {
            CourseId = courseId,
            StepKey = OnlineCourseStepKeys.ExternalPlayback,
            Provider = FirstNonEmpty(snapshot.Provider, "YouTube"),
            Status = CourseGenerationStepStatus.Failed,
            RequestJson = SerializePayload(new
            {
                lessonId,
                provider = snapshot.Provider,
                externalUrl = snapshot.ExternalUrl,
                sessionToken = snapshot.SessionToken,
                fallbackLaunched
            }),
            ErrorMessage = errorMessage
        });
    }

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return string.Empty;
    }

    private static string SerializePayload(object? value)
    {
        return value == null
            ? string.Empty
            : JsonSerializer.Serialize(value);
    }

    private static bool TryResolveYouTubeVideo(Lesson lesson, out string videoId, out string canonicalUrl)
    {
        videoId = string.Empty;
        canonicalUrl = string.Empty;

        if (string.IsNullOrWhiteSpace(lesson.ExternalUrl))
        {
            return false;
        }

        if (!Uri.TryCreate(lesson.ExternalUrl, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var host = uri.Host.Trim().ToLowerInvariant();
        if (!host.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) &&
            !host.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (host.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
        {
            videoId = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        }
        else
        {
            var segments = uri.AbsolutePath.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 2 &&
                (string.Equals(segments[0], "embed", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(segments[0], "shorts", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(segments[0], "live", StringComparison.OrdinalIgnoreCase)))
            {
                videoId = segments[1];
            }
            else
            {
                var query = ParseQuery(uri.Query);
                if (query.TryGetValue("v", out var queryVideoId))
                {
                    videoId = queryVideoId;
                }
            }
        }

        if (string.IsNullOrWhiteSpace(videoId))
        {
            return false;
        }

        canonicalUrl = $"https://www.youtube.com/watch?v={videoId}";
        return true;
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
        {
            return values;
        }

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
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

    private void RaiseStateChanged(ExternalLessonPlaybackSnapshot snapshot)
    {
        StateChanged?.Invoke(snapshot);
    }
}

public sealed record ExternalLessonPlaybackSnapshot
{
    public static ExternalLessonPlaybackSnapshot Hidden { get; } = new();

    public long SessionToken { get; init; }
    public Guid? CourseId { get; init; }
    public Guid? LessonId { get; init; }
    public string Provider { get; init; } = string.Empty;
    public string ExternalUrl { get; init; } = string.Empty;
    public string VideoId { get; init; } = string.Empty;
    public TimeSpan KnownDuration { get; init; }
    public ExternalLessonPlaybackStatus Status { get; init; } = ExternalLessonPlaybackStatus.Hidden;
    public string StatusTitle { get; init; } = "Player externo aguardando";
    public string StatusMessage { get; init; } = "O app esta aguardando a proxima aula externa.";
    public ExternalLessonPlaybackViewport? Viewport { get; init; }
    public bool ResumePositionEnabled => false;
    public bool ShouldShowStatusOverlay =>
        Status is ExternalLessonPlaybackStatus.Pending or ExternalLessonPlaybackStatus.Error;

    public bool ShouldShowExternalHost =>
        !string.IsNullOrWhiteSpace(VideoId) &&
        Viewport is { IsVisible: true };
}

public readonly record struct ExternalLessonPlaybackViewport(
    double LeftRatio,
    double TopRatio,
    double WidthRatio,
    double HeightRatio,
    bool IsVisible)
{
    public ExternalLessonPlaybackViewport Normalize()
    {
        var left = Clamp(LeftRatio);
        var top = Clamp(TopRatio);
        var width = Clamp(WidthRatio);
        var height = Clamp(HeightRatio);

        width = Math.Min(width, Math.Max(0, 1 - left));
        height = Math.Min(height, Math.Max(0, 1 - top));

        return new ExternalLessonPlaybackViewport(
            left,
            top,
            width,
            height,
            IsVisible && width > 0 && height > 0);
    }

    private static double Clamp(double value)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return 0;
        }

        return Math.Clamp(value, 0, 1);
    }
}

public sealed class ExternalLessonPlaybackCompletedEventArgs(long sessionToken, Guid courseId, Guid lessonId) : EventArgs
{
    public long SessionToken { get; } = sessionToken;
    public Guid CourseId { get; } = courseId;
    public Guid LessonId { get; } = lessonId;
}

public enum ExternalLessonPlaybackStatus
{
    Hidden,
    Pending,
    Ready,
    Playing,
    Error
}

public enum YouTubeIframePlayerState
{
    Unstarted = -1,
    Ended = 0,
    Playing = 1,
    Paused = 2,
    Buffering = 3,
    Cued = 5
}
