using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui;
using Microsoft.Maui.Controls;
using studyhub.app.services;
#if WINDOWS
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Media;
using Windows.Media.Playback;
#endif

namespace studyhub.app;

public partial class MainPage : ContentPage
{
    private const double NativeTransportControlZoneRatio = 0.24;
    private const double NativeTransportControlZoneMaxRatio = 0.45;
    private const double NativeTransportControlZoneMinHeight = 96;
    private const int NativeTransportNavigationDedupWindowMs = 350;

    private readonly ILogger<MainPage> _logger;
    private readonly NativeLessonPlaybackService _nativeLessonPlaybackService;
    private readonly SemaphoreSlim _nativeStateChangeGate = new(1, 1);
    private readonly TapGestureRecognizer _nativeHostTapGesture = new();

    private long _loadedNativeSessionToken;
    private string? _loadedNativeFilePath;
    private MediaElementState _currentNativeMediaState = MediaElementState.None;
    private double _appliedNativePlaybackSpeed = 1.0;
    private CancellationTokenSource _nativeMediaLifecycleCts = new();
    private bool _nativeMediaEventsAttached;
    private bool _isNativeSourceTransitionInProgress;

#if WINDOWS
    private global::Windows.Media.Playback.MediaPlayer? _nativeResolvedMediaPlayer;
    private MediaPlaybackCommandManager? _nativePlaybackCommandManager;
    private SystemMediaTransportControls? _nativeSystemMediaTransportControls;
    private global::Windows.Foundation.TypedEventHandler<MediaPlaybackCommandManager, MediaPlaybackCommandManagerPreviousReceivedEventArgs>? _nativePreviousReceivedHandler;
    private global::Windows.Foundation.TypedEventHandler<MediaPlaybackCommandManager, MediaPlaybackCommandManagerNextReceivedEventArgs>? _nativeNextReceivedHandler;
    private global::Windows.Foundation.TypedEventHandler<SystemMediaTransportControls, SystemMediaTransportControlsButtonPressedEventArgs>? _nativeButtonPressedHandler;
#endif

    private bool _isPageUnloading;
    private readonly object _nativeLessonNavigationDedupSync = new();
    private NativeLessonNavigationCommand? _lastNativeLessonNavigationCommand;
    private long _lastNativeLessonNavigationSessionToken;
    private long _lastNativeLessonNavigationTimestampMs = -1;

    public MainPage()
    {
        InitializeComponent();
        _nativeHostTapGesture.Tapped += HandleNativePlayerHostTapped;
        nativePlayerHost.GestureRecognizers.Add(_nativeHostTapGesture);

        _logger = IPlatformApplication.Current?.Services.GetRequiredService<ILogger<MainPage>>()
            ?? throw new InvalidOperationException("O logger do MainPage nao foi inicializado.");

        _nativeLessonPlaybackService = IPlatformApplication.Current?.Services.GetRequiredService<NativeLessonPlaybackService>()
            ?? throw new InvalidOperationException("O player nativo de aulas nao foi inicializado.");

        _nativeLessonPlaybackService.StateChanged += HandleNativePlaybackStateChanged;
        SizeChanged += HandlePageSizeChanged;
        nativeMediaElement.HandlerChanged += HandleNativeMediaElementHandlerChanged;

        UpdateNativePlayerSurface(NativeLessonPlaybackSnapshot.Hidden);
    }

    private void HandlePageSizeChanged(object? sender, EventArgs e)
    {
        UpdateNativePlayerSurface(_nativeLessonPlaybackService.Snapshot);
    }

    private void HandleNativeMediaElementHandlerChanged(object? sender, EventArgs e)
    {
#if WINDOWS
        ConfigureNativeTransportControlCommands();
#endif
    }

    private void HandleNativePlaybackStateChanged(NativeLessonPlaybackSnapshot snapshot)
    {
        if (_isPageUnloading)
        {
            return;
        }

        if (Dispatcher.IsDispatchRequired)
        {
            Dispatcher.Dispatch(() => UpdateNativePlayerSurface(snapshot));
            return;
        }

        UpdateNativePlayerSurface(snapshot);
    }

    private void UpdateNativePlayerSurface(NativeLessonPlaybackSnapshot snapshot)
    {
        if (_isPageUnloading)
        {
            return;
        }

        UpdateNativeStatusOverlay(snapshot);

        if (!snapshot.ShouldShowNativeHost || snapshot.Viewport is not { } viewport)
        {
            HideNativePlayerSurface();
            return;
        }

        var bounds = ResolveBounds(viewport.LeftRatio, viewport.TopRatio, viewport.WidthRatio, viewport.HeightRatio);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            HideNativePlayerSurface();
            return;
        }

        AbsoluteLayout.SetLayoutBounds(nativePlayerHost, bounds);
        nativePlayerHost.IsVisible = true;
        UpdateNativePlaybackControlsForState();
#if WINDOWS
        ConfigureNativeTransportControlCommands();
#endif
        ApplyNativePlaybackSpeed(snapshot);

        if (snapshot.SessionToken == _loadedNativeSessionToken &&
            string.Equals(snapshot.FilePath, _loadedNativeFilePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        LoadNativeSource(snapshot);
    }

    private void LoadNativeSource(NativeLessonPlaybackSnapshot snapshot)
    {
        BeginNativeSourceTransition();
        try
        {
            SafeStopNativeMediaElement();
            nativeMediaElement.Source = null;
            _loadedNativeSessionToken = snapshot.SessionToken;
            _loadedNativeFilePath = snapshot.FilePath;
            _currentNativeMediaState = MediaElementState.None;
            _appliedNativePlaybackSpeed = double.NaN;

            if (!string.IsNullOrWhiteSpace(snapshot.FilePath))
            {
                AttachNativeMediaElementEvents();
                nativeMediaElement.Source = MediaSource.FromFile(snapshot.FilePath);
#if WINDOWS
                ConfigureNativeTransportControlCommands();
#endif
                ApplyNativePlaybackSpeed(snapshot);
            }
        }
        finally
        {
            _isNativeSourceTransitionInProgress = false;
        }
    }

    private void HideNativePlayerSurface()
    {
        BeginNativeSourceTransition();
        nativePlayerHost.IsVisible = false;
        AbsoluteLayout.SetLayoutBounds(nativePlayerHost, new Rect(0, 0, 0, 0));

        if (_loadedNativeSessionToken == 0 && nativeMediaElement.Source == null)
        {
            _appliedNativePlaybackSpeed = 1.0;
            _isNativeSourceTransitionInProgress = false;
            return;
        }

        SafeStopNativeMediaElement();
        nativeMediaElement.Source = null;
        _loadedNativeSessionToken = 0;
        _loadedNativeFilePath = null;
        _currentNativeMediaState = MediaElementState.None;
        _appliedNativePlaybackSpeed = 1.0;
        _isNativeSourceTransitionInProgress = false;
    }

    private Rect ResolveBounds(double leftRatio, double topRatio, double widthRatio, double heightRatio)
    {
        var width = blazorWebView.Width;
        var height = blazorWebView.Height;
        if (width <= 0 || height <= 0)
        {
            return new Rect(0, 0, 0, 0);
        }

        return new Rect(
            leftRatio * width,
            topRatio * height,
            widthRatio * width,
            heightRatio * height);
    }

    private void UpdateNativeStatusOverlay(NativeLessonPlaybackSnapshot snapshot)
    {
        nativePlayerStatusOverlay.IsVisible = snapshot.ShouldShowStatusOverlay;
        nativePlayerStatusTitle.Text = snapshot.StatusTitle;
        nativePlayerStatusMessage.Text = snapshot.StatusMessage;
        nativePlayerStatusOverlay.Stroke = snapshot.Status switch
        {
            NativeLessonPlaybackStatus.Pending => Color.FromArgb("#736CE5"),
            NativeLessonPlaybackStatus.Ready => Color.FromArgb("#00B894"),
            NativeLessonPlaybackStatus.Playing => Color.FromArgb("#00B894"),
            NativeLessonPlaybackStatus.Error => Color.FromArgb("#D63031"),
            _ => Color.FromArgb("#14FFFFFF")
        };
    }

    private async void HandleNativeMediaOpened(object? sender, EventArgs e)
    {
        await RunNativeMediaEventAsync(
            "MediaOpened",
            sender,
            async cancellationToken =>
            {
                if (!ShouldProcessNativeMediaEvent(sender, cancellationToken))
                {
                    return;
                }

#if WINDOWS
                ConfigureNativeTransportControlCommands();
#endif
                await _nativeLessonPlaybackService.HandleMediaOpenedAsync(_loadedNativeSessionToken, nativeMediaElement.Duration);
                ApplyNativePlaybackSpeed(_nativeLessonPlaybackService.Snapshot);

                var initialStartOffset = await _nativeLessonPlaybackService.ConsumeInitialStartOffsetAsync(
                    _loadedNativeSessionToken,
                    nativeMediaElement.Duration);

                if (initialStartOffset > TimeSpan.Zero)
                {
                    try
                    {
                        await nativeMediaElement.SeekTo(initialStartOffset, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(
                            ex,
                            "Native startup seek failed. SessionToken: {SessionToken}. FilePath: {FilePath}. RequestedOffset: {RequestedOffset}.",
                            _loadedNativeSessionToken,
                            _loadedNativeFilePath,
                            initialStartOffset);
                    }
                }

                _logger.LogInformation(
                    "Native media opened. SessionToken: {SessionToken}. FilePath: {FilePath}. Duration: {Duration}. StartupOffsetAppliedSeconds: {StartupOffsetAppliedSeconds}.",
                    _loadedNativeSessionToken,
                    _loadedNativeFilePath,
                    nativeMediaElement.Duration,
                    Math.Round(initialStartOffset.TotalSeconds, 3, MidpointRounding.AwayFromZero));
            });
    }

    private async void HandleNativeMediaEnded(object? sender, EventArgs e)
    {
        await RunNativeMediaEventAsync(
            "MediaEnded",
            sender,
            async cancellationToken =>
            {
                if (!ShouldProcessNativeMediaEvent(sender, cancellationToken))
                {
                    return;
                }

                await _nativeLessonPlaybackService.HandleMediaEndedAsync(
                    _loadedNativeSessionToken,
                    nativeMediaElement.Position,
                    nativeMediaElement.Duration);
            });
    }

    private async void HandleNativeMediaFailed(object? sender, MediaFailedEventArgs e)
    {
        await RunNativeMediaEventAsync(
            "MediaFailed",
            sender,
            async cancellationToken =>
            {
                if (!ShouldProcessNativeMediaEvent(sender, cancellationToken))
                {
                    return;
                }

                _currentNativeMediaState = MediaElementState.Failed;
                UpdateNativePlaybackControlsForState();
                await _nativeLessonPlaybackService.HandleMediaFailedAsync(_loadedNativeSessionToken, e.ErrorMessage);
            });
    }

    private async void HandleNativeMediaPositionChanged(object? sender, MediaPositionChangedEventArgs e)
    {
        await RunNativeMediaEventAsync(
            "PositionChanged",
            sender,
            async cancellationToken =>
            {
                if (!ShouldProcessNativeMediaEvent(sender, cancellationToken))
                {
                    return;
                }

                await _nativeLessonPlaybackService.HandlePositionChangedAsync(_loadedNativeSessionToken, e.Position);
            });
    }

    private async void HandleNativeMediaStateChanged(object? sender, MediaStateChangedEventArgs e)
    {
        await RunNativeMediaEventAsync(
            "StateChanged",
            sender,
            async cancellationToken =>
            {
                if (!ShouldProcessNativeMediaEvent(sender, cancellationToken))
                {
                    return;
                }

                var gateEntered = false;
                try
                {
                    await _nativeStateChangeGate.WaitAsync(cancellationToken);
                    gateEntered = true;

                    if (!ShouldProcessNativeMediaEvent(sender, cancellationToken))
                    {
                        return;
                    }

                    _currentNativeMediaState = e.NewState;
                    UpdateNativePlaybackControlsForState();
                    await _nativeLessonPlaybackService.HandleStateChangedAsync(_loadedNativeSessionToken, e.NewState);
                }
                finally
                {
                    if (gateEntered)
                    {
                        _nativeStateChangeGate.Release();
                    }
                }
            });
    }

    private void UpdateNativePlaybackControlsForState()
    {
        if (_isPageUnloading ||
            _isNativeSourceTransitionInProgress ||
            !nativePlayerHost.IsVisible ||
            _loadedNativeSessionToken == 0)
        {
            return;
        }

        _logger.LogDebug(
            "Native playback state updated without mutating native playback controls. SessionToken: {SessionToken}. State: {State}.",
            _loadedNativeSessionToken,
            _currentNativeMediaState);
    }

#if WINDOWS
    private MediaPlayerElement? ResolveNativeMediaPlayerElement()
    {
        var platformView = nativeMediaElement.Handler?.PlatformView;
        if (platformView == null)
        {
            return null;
        }

        if (platformView is MediaPlayerElement directMediaPlayerElement)
        {
            return directMediaPlayerElement;
        }

        if (platformView is DependencyObject dependencyRoot)
        {
            var resolvedFromTree = FindMediaPlayerElementInVisualTree(
                dependencyRoot,
                new HashSet<DependencyObject>());

            if (resolvedFromTree != null)
            {
                return resolvedFromTree;
            }
        }

        return null;
    }

    private static MediaPlayerElement? FindMediaPlayerElementInVisualTree(
        DependencyObject node,
        HashSet<DependencyObject> visited)
    {
        if (!visited.Add(node))
        {
            return null;
        }

        if (node is MediaPlayerElement mediaPlayerElement)
        {
            return mediaPlayerElement;
        }

        var visualChildrenCount = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < visualChildrenCount; i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child == null)
            {
                continue;
            }

            var resolvedFromVisualChild = FindMediaPlayerElementInVisualTree(child, visited);
            if (resolvedFromVisualChild != null)
            {
                return resolvedFromVisualChild;
            }
        }

        switch (node)
        {
            case ContentControl { Content: DependencyObject contentChild }:
            {
                var resolvedFromContent = FindMediaPlayerElementInVisualTree(contentChild, visited);
                if (resolvedFromContent != null)
                {
                    return resolvedFromContent;
                }

                break;
            }

            case global::Microsoft.UI.Xaml.Controls.ContentPresenter { Content: DependencyObject presentedContent }:
            {
                var resolvedFromPresentedContent = FindMediaPlayerElementInVisualTree(presentedContent, visited);
                if (resolvedFromPresentedContent != null)
                {
                    return resolvedFromPresentedContent;
                }

                break;
            }

            case Panel panel:
            {
                foreach (var panelChild in panel.Children)
                {
                    if (panelChild is not DependencyObject dependencyChild)
                    {
                        continue;
                    }

                    var resolvedFromPanelChild = FindMediaPlayerElementInVisualTree(dependencyChild, visited);
                    if (resolvedFromPanelChild != null)
                    {
                        return resolvedFromPanelChild;
                    }
                }

                break;
            }
        }

        return null;
    }

    private void ConfigureNativeTransportControlCommands()
    {
        if (_isPageUnloading)
        {
            return;
        }

        var mediaPlayerElement = ResolveNativeMediaPlayerElement();
        if (mediaPlayerElement?.MediaPlayer == null)
        {
            DetachNativeTransportControlCommands();
            return;
        }

        var mediaPlayer = mediaPlayerElement.MediaPlayer;
        var commandManager = mediaPlayer.CommandManager;
        var systemMediaTransportControls = mediaPlayer.SystemMediaTransportControls;

        systemMediaTransportControls.IsPreviousEnabled = true;
        systemMediaTransportControls.IsNextEnabled = true;

        if (ReferenceEquals(_nativeResolvedMediaPlayer, mediaPlayer) &&
            ReferenceEquals(_nativePlaybackCommandManager, commandManager) &&
            ReferenceEquals(_nativeSystemMediaTransportControls, systemMediaTransportControls))
        {
            return;
        }

        DetachNativeTransportControlCommands();
        _nativeResolvedMediaPlayer = mediaPlayer;

        if (commandManager != null)
        {
            _nativePlaybackCommandManager = commandManager;
            _nativePreviousReceivedHandler = HandleNativePreviousTrackReceived;
            _nativeNextReceivedHandler = HandleNativeNextTrackReceived;

            _nativePlaybackCommandManager.IsEnabled = true;
            _nativePlaybackCommandManager.PreviousBehavior.EnablingRule = MediaCommandEnablingRule.Always;
            _nativePlaybackCommandManager.NextBehavior.EnablingRule = MediaCommandEnablingRule.Always;
            _nativePlaybackCommandManager.PreviousReceived += _nativePreviousReceivedHandler;
            _nativePlaybackCommandManager.NextReceived += _nativeNextReceivedHandler;
        }

        _nativeSystemMediaTransportControls = systemMediaTransportControls;
        _nativeButtonPressedHandler = HandleNativeTransportControlButtonPressed;
        _nativeSystemMediaTransportControls.ButtonPressed += _nativeButtonPressedHandler;
    }

    private void DetachNativeTransportControlCommands()
    {
        if (_nativePlaybackCommandManager != null)
        {
            if (_nativePreviousReceivedHandler != null)
            {
                _nativePlaybackCommandManager.PreviousReceived -= _nativePreviousReceivedHandler;
            }

            if (_nativeNextReceivedHandler != null)
            {
                _nativePlaybackCommandManager.NextReceived -= _nativeNextReceivedHandler;
            }
        }

        if (_nativeSystemMediaTransportControls != null && _nativeButtonPressedHandler != null)
        {
            _nativeSystemMediaTransportControls.ButtonPressed -= _nativeButtonPressedHandler;
        }

        _nativeResolvedMediaPlayer = null;
        _nativePlaybackCommandManager = null;
        _nativeSystemMediaTransportControls = null;
        _nativePreviousReceivedHandler = null;
        _nativeNextReceivedHandler = null;
        _nativeButtonPressedHandler = null;
    }

    private void HandleNativePreviousTrackReceived(
        MediaPlaybackCommandManager sender,
        MediaPlaybackCommandManagerPreviousReceivedEventArgs args)
    {
        args.Handled = true;
        RequestNativeLessonNavigation(NativeLessonNavigationCommand.Previous);
    }

    private void HandleNativeNextTrackReceived(
        MediaPlaybackCommandManager sender,
        MediaPlaybackCommandManagerNextReceivedEventArgs args)
    {
        args.Handled = true;
        RequestNativeLessonNavigation(NativeLessonNavigationCommand.Next);
    }

    private void HandleNativeTransportControlButtonPressed(
        SystemMediaTransportControls sender,
        SystemMediaTransportControlsButtonPressedEventArgs args)
    {
        switch (args.Button)
        {
            case SystemMediaTransportControlsButton.Previous:
                RequestNativeLessonNavigation(NativeLessonNavigationCommand.Previous);
                break;

            case SystemMediaTransportControlsButton.Next:
                RequestNativeLessonNavigation(NativeLessonNavigationCommand.Next);
                break;
        }
    }
#endif

    private void RequestNativeLessonNavigation(NativeLessonNavigationCommand command)
    {
        if (Dispatcher.IsDispatchRequired)
        {
            Dispatcher.Dispatch(() => RequestNativeLessonNavigation(command));
            return;
        }

        if (_isPageUnloading ||
            _isNativeSourceTransitionInProgress ||
            !nativePlayerHost.IsVisible ||
            _loadedNativeSessionToken == 0 ||
            string.IsNullOrWhiteSpace(_loadedNativeFilePath))
        {
            return;
        }

        var snapshot = _nativeLessonPlaybackService.Snapshot;
        if (snapshot.SessionToken != _loadedNativeSessionToken ||
            snapshot.Status == NativeLessonPlaybackStatus.Error)
        {
            return;
        }

        if (ShouldSuppressDuplicateNativeLessonNavigation(command, snapshot.SessionToken))
        {
            _logger.LogDebug(
                "Ignored duplicated native lesson navigation command. SessionToken: {SessionToken}. Command: {Command}.",
                snapshot.SessionToken,
                command);
            return;
        }

        _nativeLessonPlaybackService.RequestLessonNavigation(command);
    }

    private bool ShouldSuppressDuplicateNativeLessonNavigation(NativeLessonNavigationCommand command, long sessionToken)
    {
        var nowTimestampMs = Environment.TickCount64;
        lock (_nativeLessonNavigationDedupSync)
        {
            var isDuplicate =
                _lastNativeLessonNavigationCommand == command &&
                _lastNativeLessonNavigationSessionToken == sessionToken &&
                _lastNativeLessonNavigationTimestampMs >= 0 &&
                nowTimestampMs - _lastNativeLessonNavigationTimestampMs <= NativeTransportNavigationDedupWindowMs;

            _lastNativeLessonNavigationCommand = command;
            _lastNativeLessonNavigationSessionToken = sessionToken;
            _lastNativeLessonNavigationTimestampMs = nowTimestampMs;

            return isDuplicate;
        }
    }

    private void ApplyNativePlaybackSpeed(NativeLessonPlaybackSnapshot snapshot)
    {
        if (_isPageUnloading ||
            _isNativeSourceTransitionInProgress ||
            !nativePlayerHost.IsVisible ||
            _loadedNativeSessionToken == 0 ||
            snapshot.SessionToken != _loadedNativeSessionToken)
        {
            return;
        }

        var normalizedSpeed = NormalizeNativePlaybackSpeed(snapshot.PlaybackSpeed);
        if (Math.Abs(_appliedNativePlaybackSpeed - normalizedSpeed) < 0.0001)
        {
            return;
        }

        try
        {
            nativeMediaElement.Speed = normalizedSpeed;
            _appliedNativePlaybackSpeed = normalizedSpeed;

            _logger.LogDebug(
                "Native playback speed applied. SessionToken: {SessionToken}. Speed: {Speed}.",
                snapshot.SessionToken,
                normalizedSpeed);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Native playback speed update failed. SessionToken: {SessionToken}. RequestedSpeed: {Speed}.",
                snapshot.SessionToken,
                normalizedSpeed);
        }
    }

    private static double NormalizeNativePlaybackSpeed(double playbackSpeed)
    {
        return playbackSpeed switch
        {
            0.5 or 1.0 or 1.5 or 2.0 or 2.5 => playbackSpeed,
            _ => 1.0
        };
    }

    private async void HandleNativePlayerHostTapped(object? sender, TappedEventArgs e)
    {
        if (IsNativeTransportControlTap(e))
        {
            return;
        }

        var gateEntered = false;
        try
        {
            await _nativeStateChangeGate.WaitAsync();
            gateEntered = true;

            if (!CanToggleNativePlayback())
            {
                return;
            }

            var nativeSnapshot = _nativeLessonPlaybackService.Snapshot;
            if (nativeSnapshot.SessionToken != _loadedNativeSessionToken ||
                nativeSnapshot.Status == NativeLessonPlaybackStatus.Error)
            {
                return;
            }

            switch (_currentNativeMediaState)
            {
                case MediaElementState.Playing:
                    nativeMediaElement.Pause();
                    _logger.LogDebug(
                        "Native playback paused from tap-to-toggle. SessionToken: {SessionToken}.",
                        _loadedNativeSessionToken);
                    return;

                case MediaElementState.Paused:
                case MediaElementState.Stopped:
                    nativeMediaElement.Play();
                    _logger.LogDebug(
                        "Native playback resumed from tap-to-toggle. SessionToken: {SessionToken}.",
                        _loadedNativeSessionToken);
                    return;

                case MediaElementState.None:
                    if (nativeSnapshot.Status == NativeLessonPlaybackStatus.Ready)
                    {
                        nativeMediaElement.Play();
                        _logger.LogDebug(
                            "Native playback started from ready tap-to-toggle. SessionToken: {SessionToken}.",
                            _loadedNativeSessionToken);
                    }

                    return;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "Native tap-to-toggle failed. SessionToken: {SessionToken}. CurrentState: {CurrentState}.",
                _loadedNativeSessionToken,
                _currentNativeMediaState);
        }
        finally
        {
            if (gateEntered)
            {
                _nativeStateChangeGate.Release();
            }
        }
    }

    private bool CanToggleNativePlayback()
    {
        if (_isPageUnloading ||
            _isNativeSourceTransitionInProgress ||
            !nativePlayerHost.IsVisible ||
            _loadedNativeSessionToken == 0 ||
            string.IsNullOrWhiteSpace(_loadedNativeFilePath))
        {
            return false;
        }

        return _currentNativeMediaState switch
        {
            MediaElementState.Opening => false,
            MediaElementState.Buffering => false,
            MediaElementState.Failed => false,
            _ => true
        };
    }

    private bool IsNativeTransportControlTap(TappedEventArgs args)
    {
        var tapPosition = args.GetPosition(nativePlayerHost);
        if (!tapPosition.HasValue || nativePlayerHost.Height <= 0)
        {
            return false;
        }

        var controlZoneHeight = Math.Max(
            NativeTransportControlZoneMinHeight,
            nativePlayerHost.Height * NativeTransportControlZoneRatio);
        controlZoneHeight = Math.Min(
            controlZoneHeight,
            nativePlayerHost.Height * NativeTransportControlZoneMaxRatio);

        if (controlZoneHeight <= 0)
        {
            return false;
        }

        return tapPosition.Value.Y >= nativePlayerHost.Height - controlZoneHeight;
    }

    protected override void OnHandlerChanging(HandlerChangingEventArgs args)
    {
        if (args.NewHandler == null)
        {
            _isPageUnloading = true;
            _nativeHostTapGesture.Tapped -= HandleNativePlayerHostTapped;
            nativePlayerHost.GestureRecognizers.Remove(_nativeHostTapGesture);
            _nativeLessonPlaybackService.StateChanged -= HandleNativePlaybackStateChanged;
            SizeChanged -= HandlePageSizeChanged;
            nativeMediaElement.HandlerChanged -= HandleNativeMediaElementHandlerChanged;
            CancelPendingNativeMediaEvents();
            DetachNativeMediaElementEvents();
            SafeStopNativeMediaElement();
#if WINDOWS
            DetachNativeTransportControlCommands();
#endif
        }

        base.OnHandlerChanging(args);
    }

    private void AttachNativeMediaElementEvents()
    {
        if (_nativeMediaEventsAttached || _isPageUnloading)
        {
            return;
        }

        nativeMediaElement.MediaOpened += HandleNativeMediaOpened;
        nativeMediaElement.MediaEnded += HandleNativeMediaEnded;
        nativeMediaElement.MediaFailed += HandleNativeMediaFailed;
        nativeMediaElement.PositionChanged += HandleNativeMediaPositionChanged;
        nativeMediaElement.StateChanged += HandleNativeMediaStateChanged;
        _nativeMediaEventsAttached = true;
#if WINDOWS
        ConfigureNativeTransportControlCommands();
#endif
    }

    private void DetachNativeMediaElementEvents()
    {
        if (!_nativeMediaEventsAttached)
        {
            return;
        }

        nativeMediaElement.MediaOpened -= HandleNativeMediaOpened;
        nativeMediaElement.MediaEnded -= HandleNativeMediaEnded;
        nativeMediaElement.MediaFailed -= HandleNativeMediaFailed;
        nativeMediaElement.PositionChanged -= HandleNativeMediaPositionChanged;
        nativeMediaElement.StateChanged -= HandleNativeMediaStateChanged;
        _nativeMediaEventsAttached = false;
    }

    private void BeginNativeSourceTransition()
    {
        _isNativeSourceTransitionInProgress = true;
        CancelPendingNativeMediaEvents();
        DetachNativeMediaElementEvents();
        _currentNativeMediaState = MediaElementState.None;
        _appliedNativePlaybackSpeed = double.NaN;
    }

    private void SafeStopNativeMediaElement()
    {
        try
        {
            nativeMediaElement.Stop();
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Native media stop threw during source transition cleanup.");
        }
    }

    private void CancelPendingNativeMediaEvents()
    {
        try
        {
            if (!_nativeMediaLifecycleCts.IsCancellationRequested)
            {
                _nativeMediaLifecycleCts.Cancel();
            }
        }
        catch (ObjectDisposedException)
        {
        }

        _nativeMediaLifecycleCts.Dispose();
        _nativeMediaLifecycleCts = new CancellationTokenSource();
    }

    private bool ShouldProcessNativeMediaEvent(object? sender, CancellationToken cancellationToken)
    {
        return !_isPageUnloading &&
               !cancellationToken.IsCancellationRequested &&
               _nativeMediaEventsAttached &&
               !_isNativeSourceTransitionInProgress &&
               sender == nativeMediaElement &&
               nativePlayerHost.IsVisible &&
               _loadedNativeSessionToken != 0 &&
               !string.IsNullOrWhiteSpace(_loadedNativeFilePath);
    }

    private async Task RunNativeMediaEventAsync(
        string eventName,
        object? sender,
        Func<CancellationToken, Task> action)
    {
        if (_isPageUnloading)
        {
            return;
        }

        var cancellationToken = _nativeMediaLifecycleCts.Token;

        if (Dispatcher.IsDispatchRequired)
        {
            var dispatched = Dispatcher.DispatchAsync(() => RunNativeMediaEventCoreAsync(eventName, sender, action, cancellationToken));
            await dispatched;
            return;
        }

        await RunNativeMediaEventCoreAsync(eventName, sender, action, cancellationToken);
    }

    private async Task RunNativeMediaEventCoreAsync(
        string eventName,
        object? sender,
        Func<CancellationToken, Task> action,
        CancellationToken cancellationToken)
    {
        try
        {
            await action(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(
                "Native media event {EventName} was canceled during source transition or page unload. SessionToken: {SessionToken}.",
                eventName,
                _loadedNativeSessionToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Native media event {EventName} failed. SessionToken: {SessionToken}. FilePath: {FilePath}. SourceTransition: {SourceTransition}. SenderMatches: {SenderMatches}.",
                eventName,
                _loadedNativeSessionToken,
                _loadedNativeFilePath,
                _isNativeSourceTransitionInProgress,
                sender == nativeMediaElement);
        }
    }
}
