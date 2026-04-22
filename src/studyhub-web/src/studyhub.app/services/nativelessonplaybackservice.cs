using CommunityToolkit.Maui.Core;
using Microsoft.Extensions.Logging;
using studyhub.application.Interfaces;
using studyhub.domain.Entities;

namespace studyhub.app.services;

public sealed class NativeLessonPlaybackService(
    IProgressService progressService,
    ILogger<NativeLessonPlaybackService> logger)
{
    private static readonly double[] SupportedPlaybackSpeeds = [0.5, 1.0, 1.5, 2.0, 2.5];

    private readonly IProgressService _progressService = progressService;
    private readonly ILogger<NativeLessonPlaybackService> _logger = logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private NativeLessonPlaybackSnapshot _snapshot = NativeLessonPlaybackSnapshot.Hidden;
    private long _nextSessionToken;
    private int _lastPersistedSecond = -1;
    private bool _completionRaised;
    private bool _initialStartOffsetConsumed;

    public event Action<NativeLessonPlaybackSnapshot>? StateChanged;
    public event EventHandler<NativeLessonPlaybackCompletedEventArgs>? LessonCompleted;
    public event Action<NativeLessonNavigationCommand>? LessonNavigationRequested;

    public NativeLessonPlaybackSnapshot Snapshot => _snapshot;

    public void RequestLessonNavigation(NativeLessonNavigationCommand command)
    {
        var snapshot = _snapshot;
        if (snapshot.SessionToken <= 0 ||
            snapshot.LessonId == null ||
            snapshot.Status == NativeLessonPlaybackStatus.Error)
        {
            return;
        }

        LessonNavigationRequested?.Invoke(command);
    }

    public async Task<long> ActivateAsync(
        Guid courseId,
        Lesson? lesson,
        double playbackSpeed,
        TimeSpan initialStartOffset)
    {
        NativeLessonPlaybackSnapshot updatedSnapshot;

        await _gate.WaitAsync();
        try
        {
            var sessionToken = Interlocked.Increment(ref _nextSessionToken);
            _completionRaised = false;
            _initialStartOffsetConsumed = false;
            _lastPersistedSecond = lesson == null
                ? -1
                : (int)Math.Round(Math.Max(0, lesson.LastPlaybackPosition.TotalSeconds));

            updatedSnapshot = BuildActivationSnapshot(
                sessionToken,
                courseId,
                lesson,
                playbackSpeed,
                initialStartOffset);
            _snapshot = updatedSnapshot;
        }
        finally
        {
            _gate.Release();
        }

        _logger.LogInformation(
            "Native local lesson playback activated. CourseId: {CourseId}. LessonId: {LessonId}. Status: {Status}. FilePath: {FilePath}",
            courseId,
            updatedSnapshot.LessonId,
            updatedSnapshot.Status,
            updatedSnapshot.FilePath);

        RaiseStateChanged(updatedSnapshot);
        return updatedSnapshot.SessionToken;
    }

    public async Task SetPlaybackSpeedAsync(long sessionToken, double playbackSpeed)
    {
        NativeLessonPlaybackSnapshot? changedSnapshot = null;

        await _gate.WaitAsync();
        try
        {
            if (_snapshot.SessionToken != sessionToken)
            {
                return;
            }

            var normalizedSpeed = NormalizePlaybackSpeed(playbackSpeed);
            if (Math.Abs(_snapshot.PlaybackSpeed - normalizedSpeed) < 0.0001)
            {
                return;
            }

            changedSnapshot = _snapshot with { PlaybackSpeed = normalizedSpeed };
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

    public async Task UpdateViewportAsync(long sessionToken, NativeLessonPlaybackViewport viewport)
    {
        NativeLessonPlaybackSnapshot? changedSnapshot = null;

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

    public async Task HandleMediaOpenedAsync(long sessionToken, TimeSpan duration)
    {
        NativeLessonPlaybackSnapshot snapshotAfterOpen;

        await _gate.WaitAsync();
        try
        {
            if (_snapshot.SessionToken != sessionToken)
            {
                return;
            }

            var resolvedDuration = duration > TimeSpan.Zero
                ? duration
                : _snapshot.KnownDuration;
            var resumePosition = ClampResumePosition(_snapshot.ResumePosition, resolvedDuration);

            snapshotAfterOpen = _snapshot with
            {
                KnownDuration = resolvedDuration,
                ResumePosition = resumePosition,
                Status = NativeLessonPlaybackStatus.Ready,
                StatusTitle = "Arquivo carregado",
                StatusMessage = resolvedDuration > TimeSpan.Zero
                    ? $"Video local pronto no player nativo do Windows. Duracao detectada: {resolvedDuration:hh\\:mm\\:ss}."
                    : "Video local pronto no player nativo do Windows."
            };

            _snapshot = snapshotAfterOpen;
        }
        finally
        {
            _gate.Release();
        }

        RaiseStateChanged(snapshotAfterOpen);
    }

    public async Task<TimeSpan> ConsumeInitialStartOffsetAsync(long sessionToken, TimeSpan duration)
    {
        await _gate.WaitAsync();
        try
        {
            if (_snapshot.SessionToken != sessionToken || _initialStartOffsetConsumed)
            {
                return TimeSpan.Zero;
            }

            _initialStartOffsetConsumed = true;

            var resolvedDuration = duration > TimeSpan.Zero ? duration : _snapshot.KnownDuration;
            return ClampResumePosition(_snapshot.InitialStartOffset, resolvedDuration);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task HandlePositionChangedAsync(long sessionToken, TimeSpan position)
    {
        NativeLessonPlaybackSnapshot snapshotForPersistence;
        bool shouldPersist;
        bool shouldRaiseState;

        await _gate.WaitAsync();
        try
        {
            if (_snapshot.SessionToken != sessionToken)
            {
                return;
            }

            shouldPersist = ShouldPersist(position);
            if (shouldPersist)
            {
                _lastPersistedSecond = (int)Math.Round(Math.Max(0, position.TotalSeconds));
            }

            shouldRaiseState = _snapshot.Status != NativeLessonPlaybackStatus.Playing && position > TimeSpan.Zero;
            if (shouldRaiseState)
            {
                _snapshot = _snapshot with
                {
                    Status = NativeLessonPlaybackStatus.Playing,
                    StatusTitle = "Reproducao iniciada",
                    StatusMessage = "O video local esta rodando no player nativo do Windows."
                };
            }

            snapshotForPersistence = _snapshot;
        }
        finally
        {
            _gate.Release();
        }

        if (shouldRaiseState)
        {
            RaiseStateChanged(snapshotForPersistence);
        }

        if (!shouldPersist ||
            snapshotForPersistence.CourseId == null ||
            snapshotForPersistence.LessonId == null)
        {
            return;
        }

        await _progressService.UpdateLessonPlaybackAsync(
            snapshotForPersistence.CourseId.Value,
            snapshotForPersistence.LessonId.Value,
            position,
            snapshotForPersistence.KnownDuration);
    }

    public async Task HandleStateChangedAsync(long sessionToken, MediaElementState state)
    {
        NativeLessonPlaybackSnapshot? changedSnapshot = null;

        await _gate.WaitAsync();
        try
        {
            if (_snapshot.SessionToken != sessionToken)
            {
                return;
            }

            var nextSnapshot = state switch
            {
                MediaElementState.Opening => _snapshot with
                {
                    Status = NativeLessonPlaybackStatus.Pending,
                    StatusTitle = "Abrindo video local",
                    StatusMessage = "O player nativo do Windows esta preparando a aula."
                },
                MediaElementState.Buffering => _snapshot with
                {
                    Status = NativeLessonPlaybackStatus.Pending,
                    StatusTitle = "Carregando video local",
                    StatusMessage = "O player nativo esta bufferizando a aula."
                },
                MediaElementState.Playing => _snapshot with
                {
                    Status = NativeLessonPlaybackStatus.Playing,
                    StatusTitle = "Reproducao iniciada",
                    StatusMessage = "O video local esta rodando no player nativo do Windows."
                },
                MediaElementState.Paused => _snapshot with
                {
                    Status = NativeLessonPlaybackStatus.Ready,
                    StatusTitle = "Video pausado",
                    StatusMessage = "A aula continua pronta no player nativo do Windows."
                },
                MediaElementState.Stopped => _snapshot with
                {
                    Status = NativeLessonPlaybackStatus.Ready,
                    StatusTitle = "Video pronto",
                    StatusMessage = "A aula local segue carregada no player nativo do Windows."
                },
                MediaElementState.Failed => _snapshot with
                {
                    Status = NativeLessonPlaybackStatus.Error,
                    StatusTitle = "Falha no player nativo",
                    StatusMessage = "O MediaElement do Windows nao conseguiu abrir ou reproduzir a aula local."
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

    public async Task HandleMediaEndedAsync(long sessionToken, TimeSpan position, TimeSpan duration)
    {
        NativeLessonPlaybackSnapshot snapshotAfterCompletion;
        NativeLessonPlaybackCompletedEventArgs? completionArgs = null;

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
            var resolvedPosition = resolvedDuration > TimeSpan.Zero ? resolvedDuration : position;

            _lastPersistedSecond = (int)Math.Round(Math.Max(0, resolvedPosition.TotalSeconds));
            snapshotAfterCompletion = _snapshot with
            {
                KnownDuration = resolvedDuration,
                ResumePosition = resolvedPosition,
                Status = NativeLessonPlaybackStatus.Ready,
                StatusTitle = "Aula concluida",
                StatusMessage = "A reproducao terminou no player nativo do Windows e o progresso foi salvo."
            };

            _snapshot = snapshotAfterCompletion;

            if (!_completionRaised)
            {
                _completionRaised = true;
                completionArgs = new NativeLessonPlaybackCompletedEventArgs(
                    snapshotAfterCompletion.SessionToken,
                    snapshotAfterCompletion.CourseId.Value,
                    snapshotAfterCompletion.LessonId.Value);
            }
        }
        finally
        {
            _gate.Release();
        }

        await _progressService.UpdateLessonPlaybackAsync(
            snapshotAfterCompletion.CourseId!.Value,
            snapshotAfterCompletion.LessonId!.Value,
            snapshotAfterCompletion.ResumePosition,
            snapshotAfterCompletion.KnownDuration,
            true);

        RaiseStateChanged(snapshotAfterCompletion);
        if (completionArgs != null)
        {
            LessonCompleted?.Invoke(this, completionArgs);
        }
    }

    public async Task HandleMediaFailedAsync(long sessionToken, string? reason)
    {
        NativeLessonPlaybackSnapshot? changedSnapshot = null;

        await _gate.WaitAsync();
        try
        {
            if (_snapshot.SessionToken != sessionToken)
            {
                return;
            }

            changedSnapshot = _snapshot with
            {
                Status = NativeLessonPlaybackStatus.Error,
                StatusTitle = "Falha no player nativo",
                StatusMessage = string.IsNullOrWhiteSpace(reason)
                    ? "O MediaElement do Windows nao conseguiu abrir a aula local."
                    : $"O MediaElement do Windows falhou ao abrir a aula local: {reason}"
            };

            _snapshot = changedSnapshot;
        }
        finally
        {
            _gate.Release();
        }

        _logger.LogWarning(
            "Native Windows lesson playback failed. SessionToken: {SessionToken}. FilePath: {FilePath}. Reason: {Reason}",
            sessionToken,
            changedSnapshot?.FilePath,
            reason);

        if (changedSnapshot != null)
        {
            RaiseStateChanged(changedSnapshot);
        }
    }

    public async Task ReportBridgeFailureAsync(long sessionToken, string title, string message)
    {
        NativeLessonPlaybackSnapshot? changedSnapshot = null;

        await _gate.WaitAsync();
        try
        {
            if (_snapshot.SessionToken != sessionToken)
            {
                return;
            }

            changedSnapshot = _snapshot with
            {
                Status = NativeLessonPlaybackStatus.Error,
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
            RaiseStateChanged(changedSnapshot);
        }
    }

    public async Task DeactivateAsync(long sessionToken)
    {
        NativeLessonPlaybackSnapshot changedSnapshot;

        await _gate.WaitAsync();
        try
        {
            if (_snapshot.SessionToken != sessionToken)
            {
                return;
            }

            _completionRaised = false;
            _initialStartOffsetConsumed = false;
            _lastPersistedSecond = -1;
            changedSnapshot = NativeLessonPlaybackSnapshot.Hidden;
            _snapshot = changedSnapshot;
        }
        finally
        {
            _gate.Release();
        }

        _logger.LogInformation(
            "Native local lesson playback deactivated. SessionToken: {SessionToken}",
            sessionToken);

        RaiseStateChanged(changedSnapshot);
    }

    private NativeLessonPlaybackSnapshot BuildActivationSnapshot(
        long sessionToken,
        Guid courseId,
        Lesson? lesson,
        double playbackSpeed,
        TimeSpan initialStartOffset)
    {
        var normalizedSpeed = NormalizePlaybackSpeed(playbackSpeed);
        var normalizedInitialStartOffset = initialStartOffset < TimeSpan.Zero
            ? TimeSpan.Zero
            : initialStartOffset;

        if (lesson == null)
        {
            return NativeLessonPlaybackSnapshot.Hidden with
            {
                SessionToken = sessionToken,
                CourseId = courseId,
                InitialStartOffset = TimeSpan.Zero,
                PlaybackSpeed = normalizedSpeed,
                Status = NativeLessonPlaybackStatus.Error,
                StatusTitle = "Aula indisponivel",
                StatusMessage = "Nenhum arquivo de video foi associado a esta aula."
            };
        }

        if (!OperatingSystem.IsWindows())
        {
            return NativeLessonPlaybackSnapshot.Hidden with
            {
                SessionToken = sessionToken,
                CourseId = courseId,
                LessonId = lesson.Id,
                FilePath = lesson.LocalFilePath,
                InitialStartOffset = TimeSpan.Zero,
                PlaybackSpeed = normalizedSpeed,
                ResumePosition = lesson.LastPlaybackPosition,
                KnownDuration = lesson.Duration,
                Status = NativeLessonPlaybackStatus.Error,
                StatusTitle = "Player nativo indisponivel",
                StatusMessage = "A reproducao local desta aula exige o host nativo do Windows. Nenhum fallback HTML sera usado."
            };
        }

        if (lesson.SourceType != LessonSourceType.LocalFile)
        {
            return NativeLessonPlaybackSnapshot.Hidden with
            {
                SessionToken = sessionToken,
                CourseId = courseId,
                LessonId = lesson.Id,
                InitialStartOffset = TimeSpan.Zero,
                PlaybackSpeed = normalizedSpeed,
                ResumePosition = lesson.LastPlaybackPosition,
                KnownDuration = lesson.Duration,
                Status = NativeLessonPlaybackStatus.Error,
                StatusTitle = "Fonte de aula invalida",
                StatusMessage = "A aula selecionada nao pertence ao player nativo de arquivos locais."
            };
        }

        if (string.IsNullOrWhiteSpace(lesson.LocalFilePath) || !File.Exists(lesson.LocalFilePath))
        {
            return NativeLessonPlaybackSnapshot.Hidden with
            {
                SessionToken = sessionToken,
                CourseId = courseId,
                LessonId = lesson.Id,
                FilePath = lesson.LocalFilePath,
                InitialStartOffset = TimeSpan.Zero,
                PlaybackSpeed = normalizedSpeed,
                ResumePosition = lesson.LastPlaybackPosition,
                KnownDuration = lesson.Duration,
                Status = NativeLessonPlaybackStatus.Error,
                StatusTitle = "Arquivo nao encontrado",
                StatusMessage = "O video local da aula nao foi encontrado no disco. Nenhum fallback HTML sera usado."
            };
        }

        var fileLabel = Path.GetFileName(lesson.LocalFilePath);
        return NativeLessonPlaybackSnapshot.Hidden with
        {
            SessionToken = sessionToken,
            CourseId = courseId,
            LessonId = lesson.Id,
            FilePath = lesson.LocalFilePath,
            InitialStartOffset = normalizedInitialStartOffset,
            PlaybackSpeed = normalizedSpeed,
            ResumePosition = lesson.LastPlaybackPosition,
            KnownDuration = lesson.Duration,
            Status = NativeLessonPlaybackStatus.Pending,
            StatusTitle = "Motor nativo ativo",
            StatusMessage = $"Abrindo {fileLabel} com o MediaElement nativo do Windows."
        };
    }

    private bool ShouldPersist(TimeSpan position)
    {
        var currentSecond = (int)Math.Round(Math.Max(0, position.TotalSeconds));
        if (currentSecond <= 0 && _lastPersistedSecond <= 0)
        {
            return false;
        }

        if (_lastPersistedSecond < 0)
        {
            return true;
        }

        return Math.Abs(currentSecond - _lastPersistedSecond) >= 5;
    }

    private static TimeSpan ClampResumePosition(TimeSpan resumePosition, TimeSpan duration)
    {
        if (resumePosition < TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        if (duration <= TimeSpan.Zero)
        {
            return resumePosition;
        }

        return resumePosition > duration ? duration : resumePosition;
    }

    private static double NormalizePlaybackSpeed(double playbackSpeed)
    {
        foreach (var supportedSpeed in SupportedPlaybackSpeeds)
        {
            if (Math.Abs(supportedSpeed - playbackSpeed) < 0.0001)
            {
                return supportedSpeed;
            }
        }

        return 1.0;
    }

    private void RaiseStateChanged(NativeLessonPlaybackSnapshot snapshot)
    {
        StateChanged?.Invoke(snapshot);
    }
}

public sealed record NativeLessonPlaybackSnapshot
{
    public static NativeLessonPlaybackSnapshot Hidden { get; } = new();

    public long SessionToken { get; init; }
    public Guid? CourseId { get; init; }
    public Guid? LessonId { get; init; }
    public string? FilePath { get; init; }
    public TimeSpan InitialStartOffset { get; init; }
    public double PlaybackSpeed { get; init; } = 1.0;
    public TimeSpan ResumePosition { get; init; }
    public TimeSpan KnownDuration { get; init; }
    public NativeLessonPlaybackStatus Status { get; init; } = NativeLessonPlaybackStatus.Hidden;
    public string StatusTitle { get; init; } = "Player nativo aguardando";
    public string StatusMessage { get; init; } = "O app esta aguardando a proxima aula local.";
    public NativeLessonPlaybackViewport? Viewport { get; init; }
    public bool ShouldShowStatusOverlay =>
        Status is NativeLessonPlaybackStatus.Pending or NativeLessonPlaybackStatus.Error;

    public bool ShouldShowNativeHost =>
        OperatingSystem.IsWindows() &&
        !string.IsNullOrWhiteSpace(FilePath) &&
        Viewport is { IsVisible: true };
}

public readonly record struct NativeLessonPlaybackViewport(
    double LeftRatio,
    double TopRatio,
    double WidthRatio,
    double HeightRatio,
    bool IsVisible)
{
    public NativeLessonPlaybackViewport Normalize()
    {
        var left = Clamp(LeftRatio);
        var top = Clamp(TopRatio);
        var width = Clamp(WidthRatio);
        var height = Clamp(HeightRatio);

        width = Math.Min(width, Math.Max(0, 1 - left));
        height = Math.Min(height, Math.Max(0, 1 - top));

        return new NativeLessonPlaybackViewport(
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

public sealed class NativeLessonPlaybackCompletedEventArgs(long sessionToken, Guid courseId, Guid lessonId) : EventArgs
{
    public long SessionToken { get; } = sessionToken;
    public Guid CourseId { get; } = courseId;
    public Guid LessonId { get; } = lessonId;
}

public enum NativeLessonPlaybackStatus
{
    Hidden,
    Pending,
    Ready,
    Playing,
    Error
}

public enum NativeLessonNavigationCommand
{
    Previous,
    Next
}
