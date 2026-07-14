using studyhub.application.Contracts.CourseContentSync;
using studyhub.application.Contracts.CourseSourceManagement;
using studyhub.application.Interfaces;
using studyhub.domain.Entities;
using CourseEntity = studyhub.domain.Entities.Course;

namespace studyhub.app.Components.Settings;

public delegate Task<bool> CourseStorageLocationConfirmation(
    CourseSourceLocationValidationResult validation,
    CancellationToken cancellationToken);

public enum CourseStorageOperation
{
    None = 0,
    CheckingSource = 1,
    SelectingFolder = 2,
    ValidatingLocation = 3,
    ChangingLocation = 4,
    PreviewingSync = 5,
    ApplyingSync = 6
}

public enum CourseStorageVisualStatus
{
    Verifying = 0,
    Available = 1,
    NotFound = 2,
    Attention = 3,
    AccessDenied = 4,
    Invalid = 5,
    Unexpected = 6
}

public enum CourseStorageFeedbackKind
{
    None = 0,
    Info = 1,
    Success = 2,
    Warning = 3,
    Error = 4
}

public sealed class CourseStorageCardState
{
    public const string MissingContentPreservationText =
        "Conteúdo ausente não será apagado. O StudyHub manterá o progresso e o histórico, " +
        "apenas registrando que o arquivo não foi encontrado na última sincronização.";

    internal CourseStorageCardState(CourseEntity course)
    {
        Course = course;
    }

    public CourseEntity Course { get; internal set; }
    public CourseSourceStatusResult? SourceStatus { get; internal set; }
    public CourseSourceLocationValidationResult? LocationValidation { get; internal set; }
    public CourseSourceLocationChangeResult? LocationChangeResult { get; internal set; }
    public CourseContentSyncPreviewResult? SyncPreview { get; internal set; }
    public CourseContentSyncApplyResult? SyncApplyResult { get; internal set; }
    public CourseStorageOperation Operation { get; internal set; }
    public CourseStorageFeedbackKind FeedbackKind { get; internal set; }
    public string FeedbackMessage { get; internal set; } = string.Empty;

    public bool IsBusy => Operation != CourseStorageOperation.None;

    public string CurrentRootPath
    {
        get
        {
            var metadataRoot = Course.SourceMetadata.RootPath;
            return !string.IsNullOrWhiteSpace(metadataRoot)
                ? metadataRoot
                : Course.FolderPath;
        }
    }

    public int ModuleCount => Course.Modules.Count;
    public int TopicCount => Course.Modules.Sum(module => module.Topics.Count);
    public int LessonCount => Course.Modules.Sum(module => module.Topics.Sum(topic => topic.Lessons.Count));
    public int UnavailableModuleCount => Course.Modules.Count(module => !module.IsAvailable);
    public int UnavailableTopicCount => Course.Modules.Sum(module => module.Topics.Count(topic => !topic.IsAvailable));
    public int UnavailableLessonCount => Course.Modules.Sum(
        module => module.Topics.Sum(topic => topic.Lessons.Count(lesson => !lesson.IsAvailable)));
    public int UnavailableItemCount =>
        UnavailableModuleCount + UnavailableTopicCount + UnavailableLessonCount;
    public bool HasUnavailableContent => UnavailableItemCount > 0;

    public CourseStorageVisualStatus VisualStatus
    {
        get
        {
            if (IsBusy)
            {
                return CourseStorageVisualStatus.Verifying;
            }

            if (string.IsNullOrWhiteSpace(CurrentRootPath))
            {
                return CourseStorageVisualStatus.Invalid;
            }

            return SourceStatus?.Status switch
            {
                CourseSourceStatus.Available when HasUnavailableContent => CourseStorageVisualStatus.Attention,
                CourseSourceStatus.Available => CourseStorageVisualStatus.Available,
                CourseSourceStatus.NotFound => CourseStorageVisualStatus.NotFound,
                CourseSourceStatus.AccessDenied => CourseStorageVisualStatus.AccessDenied,
                CourseSourceStatus.Invalid => CourseStorageVisualStatus.Invalid,
                CourseSourceStatus.Unexpected => CourseStorageVisualStatus.Unexpected,
                _ => CourseStorageVisualStatus.Verifying
            };
        }
    }

    public string SourceStatusLabel => VisualStatus switch
    {
        CourseStorageVisualStatus.Verifying => "Verificando",
        CourseStorageVisualStatus.Available => "Disponível",
        CourseStorageVisualStatus.NotFound => "Pasta não encontrada",
        CourseStorageVisualStatus.Attention => "Atenção",
        CourseStorageVisualStatus.AccessDenied => "Erro de acesso",
        CourseStorageVisualStatus.Invalid => "Pasta não configurada",
        CourseStorageVisualStatus.Unexpected => "Erro ao verificar",
        _ => "Verificando"
    };

    public string SourceStatusMessage =>
        SourceStatus?.Message ??
        (string.IsNullOrWhiteSpace(CurrentRootPath)
            ? "A pasta de origem do curso ainda não foi configurada."
            : "Verificando a pasta de origem.");

    public bool CanChangeLocation => !IsBusy;
    public bool CanSync =>
        !IsBusy &&
        !string.IsNullOrWhiteSpace(CurrentRootPath) &&
        SourceStatus?.Status == CourseSourceStatus.Available;

    public bool CanApplySync =>
        !IsBusy &&
        SyncApplyResult is null &&
        SyncPreview?.Success == true &&
        (SyncPreview.HasChanges || SyncPreview.Status == CourseContentSyncPreviewStatus.NoChanges);

    public string ApplyActionLabel => SyncPreview?.Status == CourseContentSyncPreviewStatus.NoChanges
        ? "Concluir verificação"
        : "Aplicar sincronização";

    public bool HasMissingPreviewContent =>
        SyncPreview is not null &&
        (SyncPreview.MissingModuleCount > 0 ||
         SyncPreview.MissingTopicCount > 0 ||
         SyncPreview.MissingLessonCount > 0);

    public string MissingContentPreservationMessage =>
        HasMissingPreviewContent ? MissingContentPreservationText : string.Empty;
}

public sealed class CourseStorageManagementController : IDisposable
{
    private readonly ICourseService _courseService;
    private readonly IFolderPickerService _folderPickerService;
    private readonly ICourseSourceManagementService _sourceManagementService;
    private readonly ICourseContentSyncService _contentSyncService;
    private readonly Action? _notifyCatalogChanged;
    private readonly CancellationTokenSource _lifetimeCancellation = new();
    private readonly object _stateLock = new();
    private readonly Dictionary<Guid, CourseStorageCardState> _cards = [];
    private readonly List<Guid> _cardOrder = [];
    private bool _isLoading;
    private bool _disposed;
    private string _loadErrorMessage = string.Empty;

    public CourseStorageManagementController(
        ICourseService courseService,
        IFolderPickerService folderPickerService,
        ICourseSourceManagementService sourceManagementService,
        ICourseContentSyncService contentSyncService,
        Action? notifyCatalogChanged = null)
    {
        _courseService = courseService;
        _folderPickerService = folderPickerService;
        _sourceManagementService = sourceManagementService;
        _contentSyncService = contentSyncService;
        _notifyCatalogChanged = notifyCatalogChanged;
    }

    public event Action? Changed;

    public IReadOnlyList<CourseStorageCardState> Cards
    {
        get
        {
            lock (_stateLock)
            {
                return _cardOrder
                    .Where(_cards.ContainsKey)
                    .Select(courseId => _cards[courseId])
                    .ToArray();
            }
        }
    }

    public bool IsLoading
    {
        get
        {
            lock (_stateLock)
            {
                return _isLoading;
            }
        }
    }

    public string LoadErrorMessage
    {
        get
        {
            lock (_stateLock)
            {
                return _loadErrorMessage;
            }
        }
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        lock (_stateLock)
        {
            if (_isLoading)
            {
                return;
            }

            _isLoading = true;
            _loadErrorMessage = string.Empty;
        }

        PublishChanged();

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _lifetimeCancellation.Token,
            cancellationToken);
        var token = linkedCancellation.Token;

        try
        {
            var courses = await _courseService.GetAllCoursesAsync();
            token.ThrowIfCancellationRequested();

            var localCourses = courses
                .Where(course => course.SourceType == CourseSourceType.LocalFolder)
                .ToList();

            lock (_stateLock)
            {
                _cards.Clear();
                _cardOrder.Clear();

                foreach (var course in localCourses)
                {
                    var card = new CourseStorageCardState(course)
                    {
                        Operation = CourseStorageOperation.CheckingSource
                    };
                    _cards.Add(course.Id, card);
                    _cardOrder.Add(course.Id);
                }
            }

            PublishChanged();

            await Task.WhenAll(localCourses.Select(course =>
                LoadSourceStatusAsync(course.Id, token)));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Component disposal and caller cancellation are intentionally quiet.
        }
        catch (Exception ex)
        {
            lock (_stateLock)
            {
                _loadErrorMessage = string.IsNullOrWhiteSpace(ex.Message)
                    ? "Não foi possível carregar os cursos locais."
                    : ex.Message;
            }
        }
        finally
        {
            lock (_stateLock)
            {
                if (!_disposed)
                {
                    _isLoading = false;
                }
            }

            PublishChanged();
        }
    }

    public async Task SelectLocationAsync(
        Guid courseId,
        CourseStorageLocationConfirmation confirmLocationAsync,
        CourseStorageLocationConfirmation confirmPartialMatchAsync,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(confirmLocationAsync);
        ArgumentNullException.ThrowIfNull(confirmPartialMatchAsync);
        ThrowIfDisposed();

        if (!TryBeginOperation(courseId, CourseStorageOperation.SelectingFolder, out var card))
        {
            return;
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _lifetimeCancellation.Token,
            cancellationToken);
        var token = linkedCancellation.Token;

        try
        {
            ClearOperationResults(card, clearLocation: true, clearSync: false);
            PublishChanged();

            var selectedFolder = await _folderPickerService.PickFolderAsync(token);
            token.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(selectedFolder))
            {
                return;
            }

            SetOperation(card, CourseStorageOperation.ValidatingLocation);

            var validation = await _sourceManagementService.ValidateLocationAsync(
                courseId,
                selectedFolder,
                token);
            token.ThrowIfCancellationRequested();

            card.LocationValidation = validation;
            SetFeedbackFromValidation(card, validation);
            PublishChanged();

            if (!CanRequestLocationChange(validation))
            {
                return;
            }

            if (!await confirmLocationAsync(validation, token))
            {
                return;
            }

            var confirmPartialMatch = false;
            if (validation.Compatibility == CourseSourceLocationCompatibility.PartialMatch)
            {
                confirmPartialMatch = await confirmPartialMatchAsync(validation, token);
                if (!confirmPartialMatch)
                {
                    return;
                }
            }

            token.ThrowIfCancellationRequested();
            SetOperation(card, CourseStorageOperation.ChangingLocation);

            var changeResult = await _sourceManagementService.ChangeLocationAsync(
                new ChangeCourseSourceLocationRequest
                {
                    CourseId = courseId,
                    FolderPath = selectedFolder,
                    ConfirmPartialMatch = confirmPartialMatch
                },
                token);
            token.ThrowIfCancellationRequested();

            card.LocationChangeResult = changeResult;

            if (!changeResult.Success)
            {
                card.LocationValidation = changeResult.Validation;
                SetFeedback(
                    card,
                    changeResult.Status == CourseSourceLocationChangeStatus.PartialConfirmationRequired
                        ? CourseStorageFeedbackKind.Warning
                        : CourseStorageFeedbackKind.Error,
                    DefaultIfEmpty(changeResult.Message, "Não foi possível alterar a localização do curso."));
                PublishChanged();
                return;
            }

            await ReloadCardAsync(card, token);
            token.ThrowIfCancellationRequested();

            card.LocationValidation = null;
            SetFeedback(
                card,
                CourseStorageFeedbackKind.Success,
                changeResult.Status == CourseSourceLocationChangeStatus.Unchanged
                    ? "A localização selecionada já estava configurada."
                    : "Localização atualizada.");
            NotifyCatalogChanged();
            PublishChanged();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Component disposal and caller cancellation are intentionally quiet.
        }
        catch (Exception ex)
        {
            SetFeedback(
                card,
                CourseStorageFeedbackKind.Error,
                DefaultIfEmpty(ex.Message, "Não foi possível verificar a nova localização."));
        }
        finally
        {
            EndOperation(card);
        }
    }

    public async Task PreviewSyncAsync(
        Guid courseId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!TryBeginOperation(courseId, CourseStorageOperation.PreviewingSync, out var card))
        {
            return;
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _lifetimeCancellation.Token,
            cancellationToken);
        var token = linkedCancellation.Token;

        try
        {
            ClearOperationResults(card, clearLocation: false, clearSync: true);

            if (string.IsNullOrWhiteSpace(card.CurrentRootPath))
            {
                SetFeedback(card, CourseStorageFeedbackKind.Error, "Localize a pasta do curso antes de sincronizar.");
                return;
            }

            var preview = await _contentSyncService.PreviewAsync(courseId, token);
            token.ThrowIfCancellationRequested();

            card.SyncPreview = preview;
            SetFeedbackFromPreview(card, preview);
            PublishChanged();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Component disposal and caller cancellation are intentionally quiet.
        }
        catch (Exception ex)
        {
            SetFeedback(
                card,
                CourseStorageFeedbackKind.Error,
                DefaultIfEmpty(ex.Message, "Não foi possível verificar o conteúdo do curso."));
        }
        finally
        {
            EndOperation(card);
        }
    }

    public async Task ApplySyncAsync(
        Guid courseId,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();

        if (!TryBeginOperation(courseId, CourseStorageOperation.ApplyingSync, out var card))
        {
            return;
        }

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(
            _lifetimeCancellation.Token,
            cancellationToken);
        var token = linkedCancellation.Token;

        try
        {
            if (card.SyncApplyResult is not null ||
                card.SyncPreview?.Success != true ||
                (!card.SyncPreview.HasChanges &&
                 card.SyncPreview.Status != CourseContentSyncPreviewStatus.NoChanges))
            {
                SetFeedback(
                    card,
                    CourseStorageFeedbackKind.Warning,
                    "Verifique o conteúdo novamente antes de aplicar a sincronização.");
                return;
            }

            card.SyncApplyResult = null;
            SetFeedback(card, CourseStorageFeedbackKind.Info, "Sincronizando...");
            PublishChanged();

            var applyResult = await _contentSyncService.ApplyAsync(courseId, token);
            token.ThrowIfCancellationRequested();

            card.SyncApplyResult = applyResult;

            if (!applyResult.Success)
            {
                SetFeedback(
                    card,
                    applyResult.Status == CourseContentSyncApplyStatus.Blocked
                        ? CourseStorageFeedbackKind.Warning
                        : CourseStorageFeedbackKind.Error,
                    DefaultIfEmpty(
                        applyResult.Message,
                        applyResult.Status == CourseContentSyncApplyStatus.Blocked
                            ? "A sincronização foi bloqueada. Faça uma nova verificação."
                            : "Não foi possível aplicar a sincronização."));
                PublishChanged();
                return;
            }

            await ReloadCardAsync(card, token);
            token.ThrowIfCancellationRequested();

            SetFeedback(
                card,
                CourseStorageFeedbackKind.Success,
                applyResult.Status == CourseContentSyncApplyStatus.NoChanges
                    ? "Nenhuma alteração estrutural foi necessária."
                    : DefaultIfEmpty(applyResult.Message, "Sincronização aplicada."));
            NotifyCatalogChanged();
            PublishChanged();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // Component disposal and caller cancellation are intentionally quiet.
        }
        catch (Exception ex)
        {
            SetFeedback(
                card,
                CourseStorageFeedbackKind.Error,
                DefaultIfEmpty(ex.Message, "Não foi possível aplicar a sincronização."));
        }
        finally
        {
            EndOperation(card);
        }
    }

    public void Dispose()
    {
        lock (_stateLock)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _lifetimeCancellation.Cancel();
        _lifetimeCancellation.Dispose();
        Changed = null;
    }

    private async Task LoadSourceStatusAsync(Guid courseId, CancellationToken cancellationToken)
    {
        CourseStorageCardState? card;
        lock (_stateLock)
        {
            _cards.TryGetValue(courseId, out card);
        }

        if (card is null)
        {
            return;
        }

        try
        {
            card.SourceStatus = await _sourceManagementService.GetSourceStatusAsync(
                courseId,
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            card.SourceStatus = new CourseSourceStatusResult
            {
                CourseId = courseId,
                RootPath = card.CurrentRootPath,
                Status = CourseSourceStatus.Unexpected,
                Message = DefaultIfEmpty(ex.Message, "Não foi possível verificar a pasta de origem.")
            };
        }
        finally
        {
            if (!_disposed)
            {
                card.Operation = CourseStorageOperation.None;
                PublishChanged();
            }
        }
    }

    private async Task ReloadCardAsync(
        CourseStorageCardState card,
        CancellationToken cancellationToken)
    {
        var reloadedCourse = await _courseService.GetCourseByIdAsync(card.Course.Id);
        cancellationToken.ThrowIfCancellationRequested();

        if (reloadedCourse is not null)
        {
            card.Course = reloadedCourse;
        }

        try
        {
            card.SourceStatus = await _sourceManagementService.GetSourceStatusAsync(
                card.Course.Id,
                cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            card.SourceStatus = new CourseSourceStatusResult
            {
                CourseId = card.Course.Id,
                RootPath = card.CurrentRootPath,
                Status = CourseSourceStatus.Unexpected,
                Message = DefaultIfEmpty(ex.Message, "Não foi possível verificar a pasta de origem.")
            };
        }
    }

    private bool TryBeginOperation(
        Guid courseId,
        CourseStorageOperation operation,
        out CourseStorageCardState card)
    {
        lock (_stateLock)
        {
            if (!_cards.TryGetValue(courseId, out card!) || card.IsBusy)
            {
                return false;
            }

            card.Operation = operation;
        }

        PublishChanged();
        return true;
    }

    private static bool CanRequestLocationChange(CourseSourceLocationValidationResult validation)
        => validation.Success &&
           !validation.IsCurrentLocation &&
           validation.Compatibility is
               CourseSourceLocationCompatibility.ExactMatch or
               CourseSourceLocationCompatibility.CompatibleWithNewContent or
               CourseSourceLocationCompatibility.PartialMatch;

    private void SetOperation(CourseStorageCardState card, CourseStorageOperation operation)
    {
        if (_disposed)
        {
            return;
        }

        card.Operation = operation;
        PublishChanged();
    }

    private void EndOperation(CourseStorageCardState card)
    {
        if (_disposed)
        {
            return;
        }

        card.Operation = CourseStorageOperation.None;
        PublishChanged();
    }

    private void SetFeedbackFromValidation(
        CourseStorageCardState card,
        CourseSourceLocationValidationResult validation)
    {
        if (!validation.Success)
        {
            SetFeedback(
                card,
                CourseStorageFeedbackKind.Error,
                DefaultIfEmpty(validation.Message, "A pasta selecionada não pôde ser validada."));
            return;
        }

        switch (validation.Compatibility)
        {
            case CourseSourceLocationCompatibility.ExactMatch:
                SetFeedback(card, CourseStorageFeedbackKind.Info, validation.Message);
                break;
            case CourseSourceLocationCompatibility.CompatibleWithNewContent:
                SetFeedback(
                    card,
                    CourseStorageFeedbackKind.Info,
                    DefaultIfEmpty(
                        validation.Message,
                        $"Todos os conteúdos conhecidos foram reconhecidos. " +
                        $"{validation.NewCandidateLessonCount} novo(s) vídeo(s) poderão ser sincronizados depois."));
                break;
            case CourseSourceLocationCompatibility.PartialMatch:
                SetFeedback(
                    card,
                    CourseStorageFeedbackKind.Warning,
                    DefaultIfEmpty(validation.Message, "Apenas parte do conteúdo conhecido foi encontrada nessa pasta."));
                break;
            case CourseSourceLocationCompatibility.Incompatible:
            case CourseSourceLocationCompatibility.InsufficientReferenceData:
                SetFeedback(
                    card,
                    CourseStorageFeedbackKind.Error,
                    DefaultIfEmpty(validation.Message, "A pasta selecionada não pode ser usada para este curso."));
                break;
            default:
                SetFeedback(
                    card,
                    CourseStorageFeedbackKind.Error,
                    DefaultIfEmpty(validation.Message, "A compatibilidade da pasta não pôde ser determinada."));
                break;
        }
    }

    private void SetFeedbackFromPreview(
        CourseStorageCardState card,
        CourseContentSyncPreviewResult preview)
    {
        if (!preview.Success)
        {
            SetFeedback(
                card,
                CourseStorageFeedbackKind.Error,
                DefaultIfEmpty(preview.Message, "Não foi possível preparar a sincronização."));
            return;
        }

        if (preview.Status == CourseContentSyncPreviewStatus.NoChanges)
        {
            SetFeedback(card, CourseStorageFeedbackKind.Success, "O conteúdo já está sincronizado.");
            return;
        }

        SetFeedback(
            card,
            preview.HasChanges ? CourseStorageFeedbackKind.Info : CourseStorageFeedbackKind.Success,
            DefaultIfEmpty(preview.Message, "Verificação de conteúdo concluída."));
    }

    private static void ClearOperationResults(
        CourseStorageCardState card,
        bool clearLocation,
        bool clearSync)
    {
        card.FeedbackKind = CourseStorageFeedbackKind.None;
        card.FeedbackMessage = string.Empty;

        if (clearLocation)
        {
            card.LocationValidation = null;
            card.LocationChangeResult = null;
        }

        if (clearSync)
        {
            card.SyncPreview = null;
            card.SyncApplyResult = null;
        }
    }

    private void SetFeedback(
        CourseStorageCardState card,
        CourseStorageFeedbackKind kind,
        string message)
    {
        if (_disposed)
        {
            return;
        }

        card.FeedbackKind = kind;
        card.FeedbackMessage = message;
        PublishChanged();
    }

    private void NotifyCatalogChanged()
    {
        if (!_disposed)
        {
            _notifyCatalogChanged?.Invoke();
        }
    }

    private void PublishChanged()
    {
        if (!_disposed)
        {
            Changed?.Invoke();
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static string DefaultIfEmpty(string? value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value;
}
