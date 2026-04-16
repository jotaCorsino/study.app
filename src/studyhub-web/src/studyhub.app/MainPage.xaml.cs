using System.Globalization;
using CommunityToolkit.Maui.Core;
using CommunityToolkit.Maui.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Maui;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.Controls;
using studyhub.app.services;
#if WINDOWS
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
#endif

namespace studyhub.app;

public partial class MainPage : ContentPage
{
    private const string ExternalBridgeScheme = "studyhub-external";
    private const double NativeTransportControlZoneRatio = 0.24;
    private const double NativeTransportControlZoneMinHeight = 96;
    private static readonly HashSet<string> ExternalFallbackErrorCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "101",
        "150",
        "153"
    };

    private readonly ILogger<MainPage> _logger;
    private readonly NativeLessonPlaybackService _nativeLessonPlaybackService;
    private readonly ExternalLessonPlaybackService _externalLessonPlaybackService;
    private readonly SemaphoreSlim _nativeStateChangeGate = new(1, 1);
    private readonly SemaphoreSlim _externalBridgeGate = new(1, 1);
    private readonly TapGestureRecognizer _nativeHostTapGesture = new();

    private long _loadedNativeSessionToken;
    private string? _loadedNativeFilePath;
    private MediaElementState _currentNativeMediaState = MediaElementState.None;
    private double _appliedNativePlaybackSpeed = 1.0;
    private CancellationTokenSource _nativeMediaLifecycleCts = new();
    private bool _nativeMediaEventsAttached;
    private bool _isNativeSourceTransitionInProgress;

    private long _loadedExternalSessionToken;
    private string? _loadedExternalVideoId;
    private double _dispatchedExternalPlaybackSpeed = 1.0;
    private double _effectiveExternalPlaybackSpeed = 1.0;
    private CancellationTokenSource _externalPlaybackLifecycleCts = new();
    private bool _externalWebViewEventsAttached;
    private bool _isExternalSourceTransitionInProgress;
    private long _externalFallbackLaunchedSessionToken;
#if WINDOWS
    private WebView2? _windowsExternalWebView;
    private bool _externalVirtualHostConfigured;
#endif

    private bool _isPageUnloading;

    public MainPage()
    {
        InitializeComponent();
        _nativeHostTapGesture.Tapped += HandleNativePlayerHostTapped;
        nativePlayerHost.GestureRecognizers.Add(_nativeHostTapGesture);

        _logger = IPlatformApplication.Current?.Services.GetRequiredService<ILogger<MainPage>>()
            ?? throw new InvalidOperationException("O logger do MainPage nao foi inicializado.");

        _nativeLessonPlaybackService = IPlatformApplication.Current?.Services.GetRequiredService<NativeLessonPlaybackService>()
            ?? throw new InvalidOperationException("O player nativo de aulas nao foi inicializado.");

        _externalLessonPlaybackService = IPlatformApplication.Current?.Services.GetRequiredService<ExternalLessonPlaybackService>()
            ?? throw new InvalidOperationException("O player externo de aulas nao foi inicializado.");

        _nativeLessonPlaybackService.StateChanged += HandleNativePlaybackStateChanged;
        _externalLessonPlaybackService.StateChanged += HandleExternalPlaybackStateChanged;
        SizeChanged += HandlePageSizeChanged;
        Loaded += HandlePageLoaded;
        externalPlayerWebView.HandlerChanged += HandleExternalPlayerWebViewHandlerChanged;

        UpdateNativePlayerSurface(NativeLessonPlaybackSnapshot.Hidden);
        UpdateExternalPlayerSurface(ExternalLessonPlaybackSnapshot.Hidden);
    }

    private void HandlePageSizeChanged(object? sender, EventArgs e)
    {
        UpdateNativePlayerSurface(_nativeLessonPlaybackService.Snapshot);
        UpdateExternalPlayerSurface(_externalLessonPlaybackService.Snapshot);
    }

    private async void HandlePageLoaded(object? sender, EventArgs e)
    {
        await EnsureExternalPlayerRuntimeReadyAsync();
    }

    private async void HandleExternalPlayerWebViewHandlerChanged(object? sender, EventArgs e)
    {
        await EnsureExternalPlayerRuntimeReadyAsync();
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

    private void HandleExternalPlaybackStateChanged(ExternalLessonPlaybackSnapshot snapshot)
    {
        if (_isPageUnloading)
        {
            return;
        }

        if (Dispatcher.IsDispatchRequired)
        {
            Dispatcher.Dispatch(() => UpdateExternalPlayerSurface(snapshot));
            return;
        }

        UpdateExternalPlayerSurface(snapshot);
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

        HideExternalPlayerSurface();

        var bounds = ResolveBounds(viewport.LeftRatio, viewport.TopRatio, viewport.WidthRatio, viewport.HeightRatio);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            HideNativePlayerSurface();
            return;
        }

        AbsoluteLayout.SetLayoutBounds(nativePlayerHost, bounds);
        nativePlayerHost.IsVisible = true;
        UpdateNativePlaybackControlsForState();
        ApplyNativePlaybackSpeed(snapshot);

        if (snapshot.SessionToken == _loadedNativeSessionToken &&
            string.Equals(snapshot.FilePath, _loadedNativeFilePath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        LoadNativeSource(snapshot);
    }

    private void UpdateExternalPlayerSurface(ExternalLessonPlaybackSnapshot snapshot)
    {
        if (_isPageUnloading)
        {
            return;
        }

        UpdateExternalStatusOverlay(snapshot);

        if (!snapshot.ShouldShowExternalHost || snapshot.Viewport is not { } viewport)
        {
            HideExternalPlayerSurface();
            return;
        }

        HideNativePlayerSurface();

        var bounds = ResolveBounds(viewport.LeftRatio, viewport.TopRatio, viewport.WidthRatio, viewport.HeightRatio);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            HideExternalPlayerSurface();
            return;
        }

        AbsoluteLayout.SetLayoutBounds(externalPlayerHost, bounds);
        externalPlayerHost.IsVisible = true;
        ApplyExternalPlaybackSpeed(snapshot);

        if (snapshot.SessionToken == _loadedExternalSessionToken &&
            string.Equals(snapshot.VideoId, _loadedExternalVideoId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        LoadExternalSource(snapshot);
    }

    private async Task EnsureExternalPlayerRuntimeReadyAsync()
    {
        if (_isPageUnloading)
        {
            return;
        }

#if WINDOWS
        if (externalPlayerWebView.Handler?.PlatformView is not WebView2 platformWebView)
        {
            return;
        }

        if (!ReferenceEquals(_windowsExternalWebView, platformWebView))
        {
            DetachWindowsExternalWebViewEvents();
            _windowsExternalWebView = platformWebView;
        }

        try
        {
            await platformWebView.EnsureCoreWebView2Async();
            ConfigureExternalVirtualHost(platformWebView.CoreWebView2);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "External YouTube runtime host initialization failed.");
        }
#endif
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
                ApplyNativePlaybackSpeed(snapshot);
            }
        }
        finally
        {
            _isNativeSourceTransitionInProgress = false;
        }
    }

    private async void LoadExternalSource(ExternalLessonPlaybackSnapshot snapshot)
    {
        BeginExternalSourceTransition();
        try
        {
            ResetExternalPlayerWebView();
            _loadedExternalSessionToken = snapshot.SessionToken;
            _loadedExternalVideoId = snapshot.VideoId;
            _externalFallbackLaunchedSessionToken = 0;
            _dispatchedExternalPlaybackSpeed = double.NaN;
            _effectiveExternalPlaybackSpeed = double.NaN;

            if (!string.IsNullOrWhiteSpace(snapshot.VideoId))
            {
                AttachExternalPlayerEvents();
                await EnsureExternalPlayerRuntimeReadyAsync();
                externalPlayerWebView.Source = new UrlWebViewSource
                {
                    Url = YouTubePlayerHostHtmlBuilder.BuildHostUrl(snapshot)
                };
            }
        }
        finally
        {
            _isExternalSourceTransitionInProgress = false;
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

    private void HideExternalPlayerSurface()
    {
        BeginExternalSourceTransition();
        externalPlayerHost.IsVisible = false;
        AbsoluteLayout.SetLayoutBounds(externalPlayerHost, new Rect(0, 0, 0, 0));

        if (_loadedExternalSessionToken == 0 && string.IsNullOrWhiteSpace(_loadedExternalVideoId))
        {
            ResetExternalPlayerWebView();
            _dispatchedExternalPlaybackSpeed = 1.0;
            _effectiveExternalPlaybackSpeed = 1.0;
            _isExternalSourceTransitionInProgress = false;
            return;
        }

        ResetExternalPlayerWebView();
        _loadedExternalSessionToken = 0;
        _loadedExternalVideoId = null;
        _externalFallbackLaunchedSessionToken = 0;
        _dispatchedExternalPlaybackSpeed = 1.0;
        _effectiveExternalPlaybackSpeed = 1.0;
        _isExternalSourceTransitionInProgress = false;
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

    private void UpdateExternalStatusOverlay(ExternalLessonPlaybackSnapshot snapshot)
    {
        externalPlayerStatusOverlay.IsVisible = snapshot.ShouldShowStatusOverlay;
        externalPlayerStatusTitle.Text = snapshot.StatusTitle;
        externalPlayerStatusMessage.Text = snapshot.StatusMessage;
        externalPlayerStatusOverlay.Stroke = snapshot.Status switch
        {
            ExternalLessonPlaybackStatus.Pending => Color.FromArgb("#736CE5"),
            ExternalLessonPlaybackStatus.Ready => Color.FromArgb("#00B894"),
            ExternalLessonPlaybackStatus.Playing => Color.FromArgb("#00B894"),
            ExternalLessonPlaybackStatus.Error => Color.FromArgb("#D63031"),
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

                await _nativeLessonPlaybackService.HandleMediaOpenedAsync(_loadedNativeSessionToken, nativeMediaElement.Duration);
                ApplyNativePlaybackSpeed(_nativeLessonPlaybackService.Snapshot);

                _logger.LogInformation(
                    "Native media opened. SessionToken: {SessionToken}. FilePath: {FilePath}. Duration: {Duration}. Automatic startup seek is disabled.",
                    _loadedNativeSessionToken,
                    _loadedNativeFilePath,
                    nativeMediaElement.Duration);
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

    private async void HandleExternalPlayerNavigating(object? sender, WebNavigatingEventArgs e)
    {
        if (_isPageUnloading || string.IsNullOrWhiteSpace(e.Url))
        {
            return;
        }

        if (!Uri.TryCreate(e.Url, UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, ExternalBridgeScheme, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        e.Cancel = true;
        await RunExternalBridgeEventAsync("Navigating", sender, uri);
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

    private void ApplyExternalPlaybackSpeed(ExternalLessonPlaybackSnapshot snapshot)
    {
        if (_isPageUnloading ||
            _isExternalSourceTransitionInProgress ||
            !externalPlayerHost.IsVisible ||
            _loadedExternalSessionToken == 0 ||
            snapshot.SessionToken != _loadedExternalSessionToken ||
            string.IsNullOrWhiteSpace(_loadedExternalVideoId))
        {
            return;
        }

        var normalizedRequestedSpeed = NormalizeExternalPlaybackSpeed(snapshot.RequestedPlaybackSpeed);
        if (Math.Abs(_dispatchedExternalPlaybackSpeed - normalizedRequestedSpeed) < 0.0001)
        {
            return;
        }

        var payload = System.Text.Json.JsonSerializer.Serialize(new
        {
            session = snapshot.SessionToken.ToString(CultureInfo.InvariantCulture),
            type = "set-rate",
            rate = normalizedRequestedSpeed.ToString("0.0##", CultureInfo.InvariantCulture)
        });

        try
        {
#if WINDOWS
            if (_windowsExternalWebView?.CoreWebView2 != null)
            {
                _windowsExternalWebView.CoreWebView2.PostWebMessageAsString(payload);
                _dispatchedExternalPlaybackSpeed = normalizedRequestedSpeed;
                return;
            }
#endif
            _ = externalPlayerWebView.EvaluateJavaScriptAsync(
                $"window.studyHubExternalPlayerBridge && window.studyHubExternalPlayerBridge.receive({payload});");
            _dispatchedExternalPlaybackSpeed = normalizedRequestedSpeed;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(
                ex,
                "External playback speed dispatch failed. SessionToken: {SessionToken}. RequestedSpeed: {RequestedSpeed}.",
                snapshot.SessionToken,
                normalizedRequestedSpeed);
        }
    }

    private static double NormalizeExternalPlaybackSpeed(double playbackSpeed)
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
            _externalLessonPlaybackService.StateChanged -= HandleExternalPlaybackStateChanged;
            Loaded -= HandlePageLoaded;
            externalPlayerWebView.HandlerChanged -= HandleExternalPlayerWebViewHandlerChanged;
            CancelPendingNativeMediaEvents();
            CancelPendingExternalPlaybackEvents();
            DetachNativeMediaElementEvents();
            DetachExternalPlayerEvents();
            SafeStopNativeMediaElement();
            ResetExternalPlayerWebView();
#if WINDOWS
            DetachWindowsExternalWebViewEvents();
            _windowsExternalWebView = null;
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

    private void AttachExternalPlayerEvents()
    {
        if (_externalWebViewEventsAttached || _isPageUnloading)
        {
            return;
        }

        externalPlayerWebView.Navigating += HandleExternalPlayerNavigating;
#if WINDOWS
        AttachWindowsExternalWebViewEvents();
#endif
        _externalWebViewEventsAttached = true;
    }

    private void DetachExternalPlayerEvents()
    {
        if (!_externalWebViewEventsAttached)
        {
            return;
        }

        externalPlayerWebView.Navigating -= HandleExternalPlayerNavigating;
#if WINDOWS
        DetachWindowsExternalWebViewEvents();
#endif
        _externalWebViewEventsAttached = false;
    }

#if WINDOWS
    private void ConfigureExternalVirtualHost(CoreWebView2? coreWebView)
    {
        if (coreWebView == null || _externalVirtualHostConfigured)
        {
            return;
        }

        var hostRootPath = Path.Combine(AppContext.BaseDirectory, "wwwroot");
        coreWebView.SetVirtualHostNameToFolderMapping(
            YouTubePlayerHostHtmlBuilder.VirtualHostName,
            hostRootPath,
            CoreWebView2HostResourceAccessKind.Allow);

        _externalVirtualHostConfigured = true;
        AttachWindowsExternalWebViewEvents();
    }

    private void AttachWindowsExternalWebViewEvents()
    {
        if (_windowsExternalWebView?.CoreWebView2 == null)
        {
            return;
        }

        _windowsExternalWebView.CoreWebView2.WebMessageReceived -= HandleExternalPlayerWebMessageReceived;
        _windowsExternalWebView.CoreWebView2.WebMessageReceived += HandleExternalPlayerWebMessageReceived;
    }

    private void DetachWindowsExternalWebViewEvents()
    {
        if (_windowsExternalWebView?.CoreWebView2 == null)
        {
            return;
        }

        _windowsExternalWebView.CoreWebView2.WebMessageReceived -= HandleExternalPlayerWebMessageReceived;
    }

    private async void HandleExternalPlayerWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (_isPageUnloading)
        {
            return;
        }

        try
        {
            var rawMessage = e.TryGetWebMessageAsString();
            if (string.IsNullOrWhiteSpace(rawMessage))
            {
                return;
            }

            var uri = BuildExternalBridgeUriFromJsonMessage(rawMessage);
            if (uri == null)
            {
                return;
            }

            await RunExternalBridgeEventAsync("WebMessageReceived", externalPlayerWebView, uri);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "External player bridge message parsing failed.");
        }
    }

    private static Uri? BuildExternalBridgeUriFromJsonMessage(string rawMessage)
    {
        using var document = System.Text.Json.JsonDocument.Parse(rawMessage);
        if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Object)
        {
            return null;
        }

        var builder = new UriBuilder($"{ExternalBridgeScheme}://event");
        var parameters = new List<string>();

        foreach (var property in document.RootElement.EnumerateObject())
        {
            var value = property.Value.ValueKind switch
            {
                System.Text.Json.JsonValueKind.String => property.Value.GetString(),
                System.Text.Json.JsonValueKind.Number => property.Value.GetRawText(),
                System.Text.Json.JsonValueKind.True => "true",
                System.Text.Json.JsonValueKind.False => "false",
                _ => null
            };

            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            parameters.Add($"{Uri.EscapeDataString(property.Name)}={Uri.EscapeDataString(value)}");
        }

        builder.Query = string.Join("&", parameters);
        return builder.Uri;
    }
#endif

    private void BeginNativeSourceTransition()
    {
        _isNativeSourceTransitionInProgress = true;
        CancelPendingNativeMediaEvents();
        DetachNativeMediaElementEvents();
        _currentNativeMediaState = MediaElementState.None;
        _appliedNativePlaybackSpeed = double.NaN;
    }

    private void BeginExternalSourceTransition()
    {
        _isExternalSourceTransitionInProgress = true;
        CancelPendingExternalPlaybackEvents();
        DetachExternalPlayerEvents();
        _dispatchedExternalPlaybackSpeed = double.NaN;
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

    private void ResetExternalPlayerWebView()
    {
        try
        {
            externalPlayerWebView.Source = new UrlWebViewSource
            {
                Url = YouTubePlayerHostHtmlBuilder.BuildBlankUrl()
            };
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "External player reset threw during source transition cleanup.");
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

    private void CancelPendingExternalPlaybackEvents()
    {
        try
        {
            if (!_externalPlaybackLifecycleCts.IsCancellationRequested)
            {
                _externalPlaybackLifecycleCts.Cancel();
            }
        }
        catch (ObjectDisposedException)
        {
        }

        _externalPlaybackLifecycleCts.Dispose();
        _externalPlaybackLifecycleCts = new CancellationTokenSource();
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

    private bool ShouldProcessExternalBridgeEvent(object? sender, CancellationToken cancellationToken)
    {
        return !_isPageUnloading &&
               !cancellationToken.IsCancellationRequested &&
               _externalWebViewEventsAttached &&
               !_isExternalSourceTransitionInProgress &&
               sender == externalPlayerWebView &&
               externalPlayerHost.IsVisible &&
               _loadedExternalSessionToken != 0 &&
               !string.IsNullOrWhiteSpace(_loadedExternalVideoId);
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

    private async Task RunExternalBridgeEventAsync(string eventName, object? sender, Uri uri)
    {
        if (_isPageUnloading)
        {
            return;
        }

        var cancellationToken = _externalPlaybackLifecycleCts.Token;

        if (Dispatcher.IsDispatchRequired)
        {
            var dispatched = Dispatcher.DispatchAsync(() => RunExternalBridgeEventCoreAsync(eventName, sender, uri, cancellationToken));
            await dispatched;
            return;
        }

        await RunExternalBridgeEventCoreAsync(eventName, sender, uri, cancellationToken);
    }

    private async Task RunExternalBridgeEventCoreAsync(
        string eventName,
        object? sender,
        Uri uri,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!ShouldProcessExternalBridgeEvent(sender, cancellationToken))
            {
                return;
            }

            var gateEntered = false;
            try
            {
                await _externalBridgeGate.WaitAsync(cancellationToken);
                gateEntered = true;

                if (!ShouldProcessExternalBridgeEvent(sender, cancellationToken))
                {
                    return;
                }

                var parameters = ParseQueryParameters(uri.Query);
                if (!parameters.TryGetValue("session", out var sessionValue) ||
                    !long.TryParse(sessionValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var sessionToken) ||
                    sessionToken != _loadedExternalSessionToken)
                {
                    return;
                }

                if (!parameters.TryGetValue("type", out var type) || string.IsNullOrWhiteSpace(type))
                {
                    return;
                }

                switch (type.Trim().ToLowerInvariant())
                {
                    case "ready":
                        await _externalLessonPlaybackService.HandlePlayerReadyAsync(
                            sessionToken,
                            ParseSeconds(parameters, "duration"));
                        _logger.LogInformation(
                            "External YouTube player ready. SessionToken: {SessionToken}. VideoId: {VideoId}. Resume position remains intentionally disabled for stability.",
                            sessionToken,
                            _loadedExternalVideoId);
                        break;

                    case "state":
                        if (parameters.TryGetValue("state", out var stateValue) &&
                            int.TryParse(stateValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var rawState) &&
                            Enum.IsDefined(typeof(YouTubeIframePlayerState), rawState))
                        {
                            await _externalLessonPlaybackService.HandlePlayerStateChangedAsync(
                                sessionToken,
                                (YouTubeIframePlayerState)rawState);
                        }
                        break;

                    case "rate":
                        var currentSnapshot = _externalLessonPlaybackService.Snapshot;
                        var requestedRate = NormalizeExternalPlaybackSpeed(
                            ParseRate(parameters, "requested", currentSnapshot.RequestedPlaybackSpeed));
                        var appliedRate = NormalizeExternalPlaybackSpeed(
                            ParseRate(parameters, "applied", requestedRate));
                        var effectiveRateChanged = Math.Abs(_effectiveExternalPlaybackSpeed - appliedRate) >= 0.0001;

                        _dispatchedExternalPlaybackSpeed = requestedRate;
                        _effectiveExternalPlaybackSpeed = appliedRate;

                        if (!effectiveRateChanged &&
                            Math.Abs(currentSnapshot.RequestedPlaybackSpeed - requestedRate) < 0.0001 &&
                            Math.Abs(currentSnapshot.EffectivePlaybackSpeed - appliedRate) < 0.0001)
                        {
                            break;
                        }

                        await _externalLessonPlaybackService.HandlePlaybackRateChangedAsync(
                            sessionToken,
                            requestedRate,
                            appliedRate);
                        break;

                    case "progress":
                        await _externalLessonPlaybackService.HandleProgressHeartbeatAsync(
                            sessionToken,
                            ParseSeconds(parameters, "position"),
                            ParseSeconds(parameters, "duration"));
                        break;

                    case "ended":
                        await _externalLessonPlaybackService.HandlePlaybackEndedAsync(
                            sessionToken,
                            ParseSeconds(parameters, "duration"));
                        break;

                    case "error":
                        var errorCode = parameters.TryGetValue("code", out var code) ? code : string.Empty;
                        var errorMessage = MapYouTubeErrorMessage(errorCode);
                        var fallbackLaunched = await TryLaunchExternalFallbackAsync(sessionToken, errorCode);
                        if (fallbackLaunched)
                        {
                            errorMessage = $"{errorMessage} O StudyHub abriu esta aula no YouTube externo para manter o curso utilizavel.";
                        }

                        await _externalLessonPlaybackService.HandleEmbedFailedAsync(
                            sessionToken,
                            errorCode,
                            errorMessage,
                            fallbackLaunched);
                        break;

                    case "bridge-error":
                        await _externalLessonPlaybackService.ReportBridgeFailureAsync(
                            sessionToken,
                            "Falha no bridge externo",
                            parameters.TryGetValue("message", out var message) && !string.IsNullOrWhiteSpace(message)
                                ? message
                                : "O bridge do player externo reportou uma falha inesperada.");
                        break;
                }
            }
            finally
            {
                if (gateEntered)
                {
                    _externalBridgeGate.Release();
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogDebug(
                "External player event {EventName} was canceled during source transition or page unload. SessionToken: {SessionToken}.",
                eventName,
                _loadedExternalSessionToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "External player event {EventName} failed. SessionToken: {SessionToken}. VideoId: {VideoId}. SourceTransition: {SourceTransition}. SenderMatches: {SenderMatches}. Url: {Url}.",
                eventName,
                _loadedExternalSessionToken,
                _loadedExternalVideoId,
                _isExternalSourceTransitionInProgress,
                sender == externalPlayerWebView,
                uri);
        }
    }

    private static Dictionary<string, string> ParseQueryParameters(string query)
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

    private static TimeSpan ParseSeconds(IReadOnlyDictionary<string, string> parameters, string key)
    {
        if (!parameters.TryGetValue(key, out var value) ||
            !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds) ||
            double.IsNaN(seconds) ||
            double.IsInfinity(seconds))
        {
            return TimeSpan.Zero;
        }

        return seconds <= 0 ? TimeSpan.Zero : TimeSpan.FromSeconds(seconds);
    }

    private static double ParseRate(IReadOnlyDictionary<string, string> parameters, string key, double fallback)
    {
        if (!parameters.TryGetValue(key, out var value) ||
            !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedRate) ||
            double.IsNaN(parsedRate) ||
            double.IsInfinity(parsedRate) ||
            parsedRate <= 0)
        {
            return fallback;
        }

        return parsedRate;
    }

    private async Task<bool> TryLaunchExternalFallbackAsync(long sessionToken, string? errorCode)
    {
        if (_externalFallbackLaunchedSessionToken == sessionToken ||
            !ExternalFallbackErrorCodes.Contains(errorCode?.Trim() ?? string.Empty))
        {
            return false;
        }

        var snapshot = _externalLessonPlaybackService.Snapshot;
        if (snapshot.SessionToken != sessionToken || string.IsNullOrWhiteSpace(snapshot.ExternalUrl))
        {
            return false;
        }

        try
        {
            await Launcher.Default.OpenAsync(snapshot.ExternalUrl);
            _externalFallbackLaunchedSessionToken = sessionToken;

            _logger.LogInformation(
                "External lesson embed fallback launched in browser. SessionToken: {SessionToken}. Url: {ExternalUrl}. ErrorCode: {ErrorCode}.",
                sessionToken,
                snapshot.ExternalUrl,
                errorCode);

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "External lesson fallback launch failed. SessionToken: {SessionToken}. Url: {ExternalUrl}. ErrorCode: {ErrorCode}.",
                sessionToken,
                snapshot.ExternalUrl,
                errorCode);

            return false;
        }
    }

    private static string MapYouTubeErrorMessage(string? code)
    {
        return code?.Trim() switch
        {
            "2" => "O YouTube rejeitou esta aula por identificador de video invalido.",
            "5" => "O YouTube nao conseguiu reproduzir esta aula no player embutido do StudyHub.",
            "100" => "O video desta aula nao esta mais disponivel no YouTube.",
            "101" => "O YouTube bloqueou a reproducao embutida desta aula no StudyHub.",
            "150" => "O YouTube bloqueou a reproducao embutida desta aula no StudyHub.",
            "153" => "O YouTube rejeitou a reproducao embutida desta aula porque o player nao recebeu identificacao de origem ou referrer valida.",
            "missing-video-id" => "A aula externa nao possui um identificador de video do YouTube valido.",
            "api-timeout" => "O player externo do YouTube demorou demais para responder no host do StudyHub.",
            "api-script-load-failed" => "O app nao conseguiu carregar a API do YouTube para esta aula externa.",
            _ => "O host externo nao conseguiu reproduzir esta aula do YouTube."
        };
    }
}
