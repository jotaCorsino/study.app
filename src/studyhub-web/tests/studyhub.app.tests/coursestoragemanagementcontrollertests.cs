using studyhub.app.Components.Settings;
using studyhub.application.Contracts.CourseContentSync;
using studyhub.application.Contracts.CourseSourceManagement;
using studyhub.application.Interfaces;
using studyhub.domain.Entities;
using studyhub.shared.Enums;
using Xunit;

namespace studyhub.app.tests;

public sealed class CourseStorageManagementControllerTests
{
    [Theory]
    [InlineData(CourseSourceStatus.Available, CourseStorageVisualStatus.Available, "Disponível")]
    [InlineData(CourseSourceStatus.NotFound, CourseStorageVisualStatus.NotFound, "Pasta não encontrada")]
    [InlineData(CourseSourceStatus.AccessDenied, CourseStorageVisualStatus.AccessDenied, "Erro de acesso")]
    [InlineData(CourseSourceStatus.Invalid, CourseStorageVisualStatus.Invalid, "Pasta não configurada")]
    public async Task LoadAsync_MapsBasicSourceStatusWithoutStartingPreview(
        CourseSourceStatus sourceStatus,
        CourseStorageVisualStatus expectedVisualStatus,
        string expectedLabel)
    {
        var course = CreateCourse();
        var sync = new FakeContentSyncService();
        using var controller = CreateController(
            new FakeCourseService(course),
            new FakeFolderPickerService(),
            new FakeSourceManagementService
            {
                StatusHandler = (courseId, _) => Task.FromResult(new CourseSourceStatusResult
                {
                    CourseId = courseId,
                    RootPath = course.FolderPath,
                    Status = sourceStatus,
                    Message = "Estado consultado."
                })
            },
            sync);

        await controller.LoadAsync();

        var card = Assert.Single(controller.Cards);
        Assert.Equal(expectedVisualStatus, card.VisualStatus);
        Assert.Equal(expectedLabel, card.SourceStatusLabel);
        Assert.Equal(0, sync.PreviewCallCount);
        Assert.Equal(0, sync.ApplyCallCount);
    }

    [Fact]
    public async Task LoadAsync_DisplaysOnlyLocalFolderCourses()
    {
        var localCourse = CreateCourse("Curso local");
        var unsupportedCourse = CreateCourse("Fonte futura");
        unsupportedCourse.SourceType = (CourseSourceType)999;
        var source = new FakeSourceManagementService();
        using var controller = CreateController(
            new FakeCourseService(localCourse, unsupportedCourse),
            new FakeFolderPickerService(),
            source,
            new FakeContentSyncService());

        await controller.LoadAsync();

        var card = Assert.Single(controller.Cards);
        Assert.Equal(localCourse.Id, card.Course.Id);
        Assert.Equal(1, source.StatusCallCount);
    }

    [Fact]
    public async Task SelectLocationAsync_PickerCancellationDoesNotValidateOrChangeAnything()
    {
        var course = CreateCourse();
        var source = new FakeSourceManagementService();
        var picker = new FakeFolderPickerService
        {
            Handler = _ => Task.FromResult<string?>(null)
        };
        using var controller = CreateController(
            new FakeCourseService(course),
            picker,
            source,
            new FakeContentSyncService());
        await controller.LoadAsync();
        var originalRoot = controller.Cards[0].CurrentRootPath;

        await controller.SelectLocationAsync(
            course.Id,
            Confirm,
            Confirm);

        var card = controller.Cards[0];
        Assert.Equal(originalRoot, card.CurrentRootPath);
        Assert.Null(card.LocationValidation);
        Assert.Equal(CourseStorageFeedbackKind.None, card.FeedbackKind);
        Assert.Equal(0, source.ValidationCallCount);
        Assert.Equal(0, source.ChangeCallCount);
    }

    [Fact]
    public async Task SelectLocationAsync_PickerCancellationDoesNotReopenPreviousBlockedResult()
    {
        var course = CreateCourse();
        var incompatiblePath = @"D:\Cursos\Incompatível";
        var selections = new Queue<string?>([incompatiblePath, null]);
        var source = new FakeSourceManagementService
        {
            ValidationResult = CreateValidation(
                course.Id,
                CourseSourceLocationCompatibility.Incompatible,
                incompatiblePath)
        };
        var picker = new FakeFolderPickerService
        {
            Handler = _ => Task.FromResult(selections.Dequeue())
        };
        using var controller = CreateController(
            new FakeCourseService(course),
            picker,
            source,
            new FakeContentSyncService());
        await controller.LoadAsync();

        await controller.SelectLocationAsync(course.Id, Confirm, Confirm);
        Assert.NotNull(controller.Cards[0].LocationValidation);

        await controller.SelectLocationAsync(course.Id, Confirm, Confirm);

        var card = controller.Cards[0];
        Assert.Null(card.LocationValidation);
        Assert.Null(card.LocationChangeResult);
        Assert.Equal(CourseStorageFeedbackKind.None, card.FeedbackKind);
        Assert.Empty(card.FeedbackMessage);
        Assert.Equal(1, source.ValidationCallCount);
        Assert.Equal(0, source.ChangeCallCount);
    }

    [Fact]
    public async Task SelectLocationAsync_ExactMatchChangesOnlyAfterConfirmationAndReloadsCard()
    {
        var course = CreateCourse(rootPath: @"C:\Cursos\Antigo");
        var reloadedCourse = CreateCourse(courseId: course.Id, rootPath: @"D:\Cursos\Novo");
        var courseService = new FakeCourseService(course)
        {
            GetByIdHandler = _ => Task.FromResult<Course?>(reloadedCourse)
        };
        var source = new FakeSourceManagementService
        {
            ValidationResult = CreateValidation(
                course.Id,
                CourseSourceLocationCompatibility.ExactMatch,
                @"D:\Cursos\Novo"),
            ChangeResult = new CourseSourceLocationChangeResult
            {
                CourseId = course.Id,
                Status = CourseSourceLocationChangeStatus.Changed,
                Message = "Origem alterada."
            }
        };
        var catalogNotifications = 0;
        using var controller = CreateController(
            courseService,
            new FakeFolderPickerService(@"D:\Cursos\Novo"),
            source,
            new FakeContentSyncService(),
            () => catalogNotifications++);
        await controller.LoadAsync();
        var confirmationCount = 0;

        await controller.SelectLocationAsync(
            course.Id,
            (validation, _) =>
            {
                confirmationCount++;
                Assert.Equal(CourseSourceLocationCompatibility.ExactMatch, validation.Compatibility);
                Assert.Equal(0, source.ChangeCallCount);
                return Task.FromResult(true);
            },
            (_, _) => throw new InvalidOperationException("ExactMatch não exige segunda confirmação."));

        var request = Assert.Single(source.ChangeRequests);
        Assert.False(request.ConfirmPartialMatch);
        Assert.Equal(1, confirmationCount);
        Assert.Equal(@"D:\Cursos\Novo", controller.Cards[0].CurrentRootPath);
        Assert.Null(controller.Cards[0].LocationValidation);
        Assert.Equal(CourseStorageFeedbackKind.Success, controller.Cards[0].FeedbackKind);
        Assert.Equal(1, courseService.GetByIdCallCount);
        Assert.Equal(1, catalogNotifications);
    }

    [Fact]
    public async Task SelectLocationAsync_CompatibleWithNewContentExposesNewItemsBeforeConfirmation()
    {
        var course = CreateCourse();
        var validation = CreateValidation(
            course.Id,
            CourseSourceLocationCompatibility.CompatibleWithNewContent,
            @"D:\Cursos\ComNovos");
        validation.NewCandidateLessonCount = 3;
        var source = new FakeSourceManagementService { ValidationResult = validation };
        var sync = new FakeContentSyncService();
        using var controller = CreateController(
            new FakeCourseService(course),
            new FakeFolderPickerService(validation.CandidateRootPath),
            source,
            sync);
        await controller.LoadAsync();

        await controller.SelectLocationAsync(
            course.Id,
            (result, _) =>
            {
                Assert.Same(result, controller.Cards[0].LocationValidation);
                Assert.Equal(3, result.NewCandidateLessonCount);
                return Task.FromResult(false);
            },
            (_, _) => throw new InvalidOperationException("CompatibleWithNewContent não exige segunda confirmação."));

        Assert.Equal(3, controller.Cards[0].LocationValidation?.NewCandidateLessonCount);
        Assert.Equal(0, source.ChangeCallCount);
        Assert.Equal(0, sync.PreviewCallCount);
        Assert.Equal(0, sync.ApplyCallCount);
    }

    [Fact]
    public async Task SelectLocationAsync_PartialMatchNeverConfirmsBackendWithoutSecondConfirmation()
    {
        var course = CreateCourse();
        var validation = CreateValidation(
            course.Id,
            CourseSourceLocationCompatibility.PartialMatch,
            @"D:\Cursos\Parcial");
        validation.MatchedExistingLessonCount = 2;
        validation.MissingExistingLessonCount = 1;
        var source = new FakeSourceManagementService { ValidationResult = validation };
        using var controller = CreateController(
            new FakeCourseService(course),
            new FakeFolderPickerService(validation.CandidateRootPath),
            source,
            new FakeContentSyncService());
        await controller.LoadAsync();
        var firstConfirmationCount = 0;
        var secondConfirmationCount = 0;

        await controller.SelectLocationAsync(
            course.Id,
            (_, _) =>
            {
                firstConfirmationCount++;
                return Task.FromResult(true);
            },
            (_, _) =>
            {
                secondConfirmationCount++;
                return Task.FromResult(false);
            });

        Assert.Equal(1, firstConfirmationCount);
        Assert.Equal(1, secondConfirmationCount);
        Assert.Empty(source.ChangeRequests);
        Assert.Equal(CourseStorageFeedbackKind.Warning, controller.Cards[0].FeedbackKind);
    }

    [Fact]
    public async Task SelectLocationAsync_PartialMatchSetsConfirmationOnlyAfterBothConfirmations()
    {
        var course = CreateCourse();
        var validation = CreateValidation(
            course.Id,
            CourseSourceLocationCompatibility.PartialMatch,
            @"D:\Cursos\Parcial");
        var source = new FakeSourceManagementService
        {
            ValidationResult = validation,
            ChangeResult = new CourseSourceLocationChangeResult
            {
                CourseId = course.Id,
                Status = CourseSourceLocationChangeStatus.Changed
            }
        };
        using var controller = CreateController(
            new FakeCourseService(course),
            new FakeFolderPickerService(validation.CandidateRootPath),
            source,
            new FakeContentSyncService());
        await controller.LoadAsync();

        await controller.SelectLocationAsync(course.Id, Confirm, Confirm);

        Assert.True(Assert.Single(source.ChangeRequests).ConfirmPartialMatch);
    }

    [Fact]
    public async Task SelectLocationAsync_IncompatibleLocationNeverCallsChange()
    {
        var course = CreateCourse();
        var validation = CreateValidation(
            course.Id,
            CourseSourceLocationCompatibility.Incompatible,
            @"D:\Cursos\Outro");
        validation.Message = "A pasta pertence a outro curso.";
        var source = new FakeSourceManagementService { ValidationResult = validation };
        using var controller = CreateController(
            new FakeCourseService(course),
            new FakeFolderPickerService(validation.CandidateRootPath),
            source,
            new FakeContentSyncService());
        await controller.LoadAsync();

        await controller.SelectLocationAsync(
            course.Id,
            (_, _) => throw new InvalidOperationException("Incompatible não pode pedir confirmação."),
            (_, _) => throw new InvalidOperationException("Incompatible não pode pedir confirmação parcial."));

        Assert.Equal(0, source.ChangeCallCount);
        Assert.Equal(CourseStorageFeedbackKind.Error, controller.Cards[0].FeedbackKind);
        Assert.Equal(validation.CandidateRootPath, controller.Cards[0].LocationValidation?.CandidateRootPath);
    }

    [Fact]
    public async Task PreviewSyncAsync_NoChangesShowsSafeCompletionActionWithoutApplying()
    {
        var course = CreateCourse();
        var sync = new FakeContentSyncService
        {
            PreviewResult = new CourseContentSyncPreviewResult
            {
                CourseId = course.Id,
                Status = CourseContentSyncPreviewStatus.NoChanges,
                Message = "Nada mudou."
            }
        };
        using var controller = CreateLoadedController(course, sync);
        await controller.LoadAsync();

        await controller.PreviewSyncAsync(course.Id);

        var card = controller.Cards[0];
        Assert.Equal(CourseContentSyncPreviewStatus.NoChanges, card.SyncPreview?.Status);
        Assert.Equal("O conteúdo já está sincronizado.", card.FeedbackMessage);
        Assert.Equal("Concluir verificação", card.ApplyActionLabel);
        Assert.True(card.CanApplySync);
        Assert.Equal(0, sync.ApplyCallCount);
    }

    [Fact]
    public async Task PreviewSyncAsync_NewContentExposesBackendCounters()
    {
        var course = CreateCourse();
        var preview = new CourseContentSyncPreviewResult
        {
            CourseId = course.Id,
            Status = CourseContentSyncPreviewStatus.Ready,
            Message = "Novos itens encontrados.",
            Modules =
            [
                new CourseContentSyncModulePlanItem { ChangeKind = CourseContentSyncChangeKind.New }
            ],
            Topics =
            [
                new CourseContentSyncTopicPlanItem { ChangeKind = CourseContentSyncChangeKind.New }
            ],
            Lessons =
            [
                new CourseContentSyncLessonPlanItem { ChangeKind = CourseContentSyncChangeKind.New },
                new CourseContentSyncLessonPlanItem { ChangeKind = CourseContentSyncChangeKind.Unchanged }
            ]
        };
        var sync = new FakeContentSyncService { PreviewResult = preview };
        using var controller = CreateLoadedController(course, sync);
        await controller.LoadAsync();

        await controller.PreviewSyncAsync(course.Id);

        var card = controller.Cards[0];
        Assert.Equal(1, card.SyncPreview?.NewModuleCount);
        Assert.Equal(1, card.SyncPreview?.NewTopicCount);
        Assert.Equal(1, card.SyncPreview?.NewLessonCount);
        Assert.Equal(1, card.SyncPreview?.UnchangedLessonCount);
        Assert.True(card.CanApplySync);
        Assert.Equal("Aplicar sincronização", card.ApplyActionLabel);
    }

    [Fact]
    public async Task PreviewSyncAsync_MissingContentExplainsThatHistoryIsPreserved()
    {
        var course = CreateCourse();
        var sync = new FakeContentSyncService
        {
            PreviewResult = new CourseContentSyncPreviewResult
            {
                CourseId = course.Id,
                Status = CourseContentSyncPreviewStatus.Ready,
                Lessons =
                [
                    new CourseContentSyncLessonPlanItem { ChangeKind = CourseContentSyncChangeKind.Missing }
                ]
            }
        };
        using var controller = CreateLoadedController(course, sync);
        await controller.LoadAsync();

        await controller.PreviewSyncAsync(course.Id);

        var card = controller.Cards[0];
        Assert.True(card.HasMissingPreviewContent);
        Assert.Contains("não será apagado", card.MissingContentPreservationMessage);
        Assert.Contains("progresso e o histórico", card.MissingContentPreservationMessage);
    }

    [Fact]
    public async Task ApplySyncAsync_SuccessReloadsCardAndNotifiesCatalog()
    {
        var course = CreateCourse();
        var reloadedCourse = CreateCourse(courseId: course.Id, moduleCount: 2);
        reloadedCourse.SourceMetadata.LastScannedAtUtc = DateTime.UtcNow;
        var courseService = new FakeCourseService(course)
        {
            GetByIdHandler = _ => Task.FromResult<Course?>(reloadedCourse)
        };
        var sync = new FakeContentSyncService
        {
            PreviewResult = ReadyPreviewWithNewLesson(course.Id),
            ApplyResult = new CourseContentSyncApplyResult
            {
                CourseId = course.Id,
                Status = CourseContentSyncApplyStatus.Applied,
                Message = "Conteúdo sincronizado.",
                CreatedModuleCount = 1,
                CreatedTopicCount = 1,
                CreatedLessonCount = 1
            }
        };
        var notifications = 0;
        using var controller = CreateController(
            courseService,
            new FakeFolderPickerService(),
            new FakeSourceManagementService(),
            sync,
            () => notifications++);
        await controller.LoadAsync();
        await controller.PreviewSyncAsync(course.Id);

        await controller.ApplySyncAsync(course.Id);

        var card = controller.Cards[0];
        Assert.Equal(2, card.ModuleCount);
        Assert.Equal(reloadedCourse.SourceMetadata.LastScannedAtUtc, card.Course.SourceMetadata.LastScannedAtUtc);
        Assert.Equal(CourseContentSyncApplyStatus.Applied, card.SyncApplyResult?.Status);
        Assert.Equal(CourseStorageFeedbackKind.Success, card.FeedbackKind);
        Assert.Equal(1, sync.ApplyCallCount);
        Assert.Equal(1, courseService.GetByIdCallCount);
        Assert.Equal(1, notifications);
    }

    [Fact]
    public async Task ApplySyncAsync_NoChangesConclusionStillCallsApplyReloadsAndNotifies()
    {
        var course = CreateCourse();
        var reloadedCourse = CreateCourse(courseId: course.Id);
        reloadedCourse.SourceMetadata.LastScannedAtUtc = DateTime.UtcNow;
        var courseService = new FakeCourseService(course)
        {
            GetByIdHandler = _ => Task.FromResult<Course?>(reloadedCourse)
        };
        var sync = new FakeContentSyncService
        {
            PreviewResult = new CourseContentSyncPreviewResult
            {
                CourseId = course.Id,
                Status = CourseContentSyncPreviewStatus.NoChanges
            },
            ApplyResult = new CourseContentSyncApplyResult
            {
                CourseId = course.Id,
                Status = CourseContentSyncApplyStatus.NoChanges,
                Message = "Verificação registrada."
            }
        };
        var notifications = 0;
        using var controller = CreateController(
            courseService,
            new FakeFolderPickerService(),
            new FakeSourceManagementService(),
            sync,
            () => notifications++);
        await controller.LoadAsync();
        await controller.PreviewSyncAsync(course.Id);

        await controller.ApplySyncAsync(course.Id);

        var card = controller.Cards[0];
        Assert.Equal(1, sync.ApplyCallCount);
        Assert.Equal(1, courseService.GetByIdCallCount);
        Assert.Equal(1, notifications);
        Assert.Equal(CourseContentSyncApplyStatus.NoChanges, card.SyncApplyResult?.Status);
        Assert.Equal(reloadedCourse.SourceMetadata.LastScannedAtUtc, card.Course.SourceMetadata.LastScannedAtUtc);
        Assert.Equal(CourseStorageFeedbackKind.Success, card.FeedbackKind);
        Assert.Equal("Nenhuma alteração estrutural foi necessária.", card.FeedbackMessage);
    }

    [Fact]
    public async Task ApplySyncAsync_BlockedDoesNotShowSuccessOrReloadCourse()
    {
        var course = CreateCourse();
        var courseService = new FakeCourseService(course);
        var sync = new FakeContentSyncService
        {
            PreviewResult = ReadyPreviewWithNewLesson(course.Id),
            ApplyResult = new CourseContentSyncApplyResult
            {
                CourseId = course.Id,
                Status = CourseContentSyncApplyStatus.Blocked,
                Message = "A origem mudou; faça um novo preview."
            }
        };
        var notifications = 0;
        using var controller = CreateController(
            courseService,
            new FakeFolderPickerService(),
            new FakeSourceManagementService(),
            sync,
            () => notifications++);
        await controller.LoadAsync();
        await controller.PreviewSyncAsync(course.Id);

        await controller.ApplySyncAsync(course.Id);

        var card = controller.Cards[0];
        Assert.Equal(CourseContentSyncApplyStatus.Blocked, card.SyncApplyResult?.Status);
        Assert.Equal(CourseStorageFeedbackKind.Warning, card.FeedbackKind);
        Assert.NotEqual(CourseStorageFeedbackKind.Success, card.FeedbackKind);
        Assert.Contains("novo preview", card.FeedbackMessage);
        Assert.Equal(0, courseService.GetByIdCallCount);
        Assert.Equal(0, notifications);
    }

    [Fact]
    public async Task ActiveCourseOperationDisablesConflictingActions()
    {
        var course = CreateCourse();
        var pickerCompletion = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var picker = new FakeFolderPickerService
        {
            Handler = _ => pickerCompletion.Task
        };
        var sync = new FakeContentSyncService();
        using var controller = CreateController(
            new FakeCourseService(course),
            picker,
            new FakeSourceManagementService(),
            sync);
        await controller.LoadAsync();

        var selectionTask = controller.SelectLocationAsync(course.Id, Confirm, Confirm);
        var card = controller.Cards[0];
        Assert.Equal(CourseStorageOperation.SelectingFolder, card.Operation);
        Assert.True(card.IsBusy);
        Assert.False(card.CanChangeLocation);
        Assert.False(card.CanSync);

        await controller.PreviewSyncAsync(course.Id);
        await controller.ApplySyncAsync(course.Id);

        Assert.Equal(0, sync.PreviewCallCount);
        Assert.Equal(0, sync.ApplyCallCount);

        pickerCompletion.SetResult(null);
        await selectionTask;
        Assert.False(card.IsBusy);
    }

    [Fact]
    public async Task LoadAsync_ServiceCancellationPropagatesAndClearsLoadingState()
    {
        var course = CreateCourse();
        var source = new FakeSourceManagementService
        {
            StatusHandler = (_, _) => Task.FromException<CourseSourceStatusResult>(
                new OperationCanceledException("Source status canceled."))
        };
        using var controller = CreateController(
            new FakeCourseService(course),
            new FakeFolderPickerService(),
            source,
            new FakeContentSyncService());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => controller.LoadAsync());

        Assert.False(controller.IsLoading);
        Assert.False(Assert.Single(controller.Cards).IsBusy);
    }

    [Fact]
    public async Task SelectLocationAsync_PickerCancellationPropagatesAndClearsOperation()
    {
        var course = CreateCourse();
        var source = new FakeSourceManagementService();
        var picker = new FakeFolderPickerService
        {
            Handler = _ => Task.FromException<string?>(
                new OperationCanceledException("Folder selection canceled."))
        };
        using var controller = CreateController(
            new FakeCourseService(course),
            picker,
            source,
            new FakeContentSyncService());
        await controller.LoadAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            controller.SelectLocationAsync(course.Id, Confirm, Confirm));

        var card = Assert.Single(controller.Cards);
        Assert.False(card.IsBusy);
        Assert.Equal(0, source.ValidationCallCount);
        Assert.Equal(0, source.ChangeCallCount);
        Assert.NotEqual(CourseStorageFeedbackKind.Success, card.FeedbackKind);
    }

    [Fact]
    public async Task PreviewSyncAsync_ServiceCancellationPropagatesWithoutPublishingResult()
    {
        var course = CreateCourse();
        var sync = new FakeContentSyncService
        {
            PreviewHandler = (_, _) => Task.FromException<CourseContentSyncPreviewResult>(
                new OperationCanceledException("Preview canceled."))
        };
        using var controller = CreateLoadedController(course, sync);
        await controller.LoadAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            controller.PreviewSyncAsync(course.Id));

        var card = Assert.Single(controller.Cards);
        Assert.False(card.IsBusy);
        Assert.Null(card.SyncPreview);
        Assert.NotEqual(CourseStorageFeedbackKind.Success, card.FeedbackKind);
    }

    [Fact]
    public async Task ApplySyncAsync_ServiceCancellationPropagatesWithoutReloadOrNotification()
    {
        var course = CreateCourse();
        var notifications = 0;
        var courseService = new FakeCourseService(course);
        var sync = new FakeContentSyncService
        {
            PreviewResult = ReadyPreviewWithNewLesson(course.Id),
            ApplyHandler = (_, _) => Task.FromException<CourseContentSyncApplyResult>(
                new OperationCanceledException("Apply canceled."))
        };
        using var controller = CreateController(
            courseService,
            new FakeFolderPickerService(),
            new FakeSourceManagementService(),
            sync,
            () => notifications++);
        await controller.LoadAsync();
        await controller.PreviewSyncAsync(course.Id);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            controller.ApplySyncAsync(course.Id));

        var card = Assert.Single(controller.Cards);
        Assert.False(card.IsBusy);
        Assert.Null(card.SyncApplyResult);
        Assert.NotEqual(CourseStorageFeedbackKind.Success, card.FeedbackKind);
        Assert.Equal(0, courseService.GetByIdCallCount);
        Assert.Equal(0, notifications);
    }

    private static CourseStorageManagementController CreateLoadedController(
        Course course,
        FakeContentSyncService sync)
        => CreateController(
            new FakeCourseService(course),
            new FakeFolderPickerService(),
            new FakeSourceManagementService(),
            sync);

    private static CourseStorageManagementController CreateController(
        ICourseService courseService,
        IFolderPickerService folderPickerService,
        ICourseSourceManagementService sourceManagementService,
        ICourseContentSyncService contentSyncService,
        Action? notifyCatalogChanged = null)
        => new(
            courseService,
            folderPickerService,
            sourceManagementService,
            contentSyncService,
            notifyCatalogChanged);

    private static Course CreateCourse(
        string title = "Curso local",
        Guid? courseId = null,
        string rootPath = @"C:\Cursos\Local",
        int moduleCount = 1)
    {
        var course = new Course
        {
            Id = courseId ?? Guid.NewGuid(),
            Title = title,
            RawTitle = title,
            SourceType = CourseSourceType.LocalFolder,
            SourceMetadata = new CourseSourceMetadata { RootPath = rootPath }
        };

        for (var moduleIndex = 0; moduleIndex < moduleCount; moduleIndex++)
        {
            var module = new Module
            {
                Id = Guid.NewGuid(),
                CourseId = course.Id,
                Title = $"Módulo {moduleIndex + 1}",
                Order = moduleIndex + 1
            };
            var topic = new Topic
            {
                Id = Guid.NewGuid(),
                ModuleId = module.Id,
                Title = $"Tópico {moduleIndex + 1}",
                Order = 1
            };
            topic.Lessons.Add(new Lesson
            {
                Id = Guid.NewGuid(),
                TopicId = topic.Id,
                Title = $"Aula {moduleIndex + 1}",
                Order = 1,
                Status = LessonStatus.NotStarted
            });
            module.Topics.Add(topic);
            course.Modules.Add(module);
        }

        return course;
    }

    private static CourseSourceLocationValidationResult CreateValidation(
        Guid courseId,
        CourseSourceLocationCompatibility compatibility,
        string candidateRootPath)
        => new()
        {
            CourseId = courseId,
            CurrentRootPath = @"C:\Cursos\Local",
            CandidateRootPath = candidateRootPath,
            ExistingLessonCount = 2,
            ExistingComparableLessonCount = 2,
            CandidateLessonCount = 2,
            MatchedExistingLessonCount = 2,
            Compatibility = compatibility,
            ErrorKind = CourseSourceLocationErrorKind.None,
            Message = "Pasta validada."
        };

    private static CourseContentSyncPreviewResult ReadyPreviewWithNewLesson(Guid courseId)
        => new()
        {
            CourseId = courseId,
            Status = CourseContentSyncPreviewStatus.Ready,
            Lessons =
            [
                new CourseContentSyncLessonPlanItem { ChangeKind = CourseContentSyncChangeKind.New }
            ]
        };

    private static Task<bool> Confirm(
        CourseSourceLocationValidationResult _,
        CancellationToken __)
        => Task.FromResult(true);

    private sealed class FakeCourseService(params Course[] courses) : ICourseService
    {
        private readonly List<Course> _courses = [.. courses];

        public Func<Guid, Task<Course?>>? GetByIdHandler { get; init; }
        public int GetByIdCallCount { get; private set; }

        public Task<List<Course>> GetAllCoursesAsync() => Task.FromResult(_courses.ToList());

        public Task<Course?> GetCourseByIdAsync(Guid id)
        {
            GetByIdCallCount++;
            return GetByIdHandler?.Invoke(id) ??
                   Task.FromResult(_courses.SingleOrDefault(course => course.Id == id));
        }

        public Task<Course?> UpdateCourseDetailsAsync(Guid id, string title, string description)
            => throw new NotSupportedException();

        public Task<Course?> UpdateCourseLifecycleStatusAsync(
            Guid id,
            CourseLifecycleStatus status,
            DateTime? changedAt = null)
            => throw new NotSupportedException();

        public Task<CourseSourceMetadata?> UpdateCourseIntroSkipPreferenceAsync(
            Guid id,
            bool introSkipEnabled,
            int introSkipSeconds)
            => throw new NotSupportedException();

        public Task<Lesson?> GetLessonByIdAsync(Guid courseId, Guid lessonId)
            => throw new NotSupportedException();

        public Task<Lesson?> GetNextLessonAsync(Guid courseId, Guid currentLessonId)
            => throw new NotSupportedException();

        public Task<Lesson?> GetPreviousLessonAsync(Guid courseId, Guid currentLessonId)
            => throw new NotSupportedException();

        public Task DeleteCourseAsync(Guid id) => throw new NotSupportedException();
    }

    private sealed class FakeFolderPickerService : IFolderPickerService
    {
        private readonly string? _selectedFolder;

        public FakeFolderPickerService(string? selectedFolder = null)
        {
            _selectedFolder = selectedFolder;
        }

        public Func<CancellationToken, Task<string?>>? Handler { get; init; }

        public Task<string?> PickFolderAsync(CancellationToken cancellationToken = default)
            => Handler?.Invoke(cancellationToken) ?? Task.FromResult(_selectedFolder);
    }

    private sealed class FakeSourceManagementService : ICourseSourceManagementService
    {
        public Func<Guid, CancellationToken, Task<CourseSourceStatusResult>>? StatusHandler { get; init; }
        public CourseSourceLocationValidationResult? ValidationResult { get; init; }
        public CourseSourceLocationChangeResult? ChangeResult { get; init; }
        public List<ChangeCourseSourceLocationRequest> ChangeRequests { get; } = [];
        public int StatusCallCount { get; private set; }
        public int ValidationCallCount { get; private set; }
        public int ChangeCallCount => ChangeRequests.Count;

        public Task<CourseSourceStatusResult> GetSourceStatusAsync(
            Guid courseId,
            CancellationToken cancellationToken = default)
        {
            StatusCallCount++;
            return StatusHandler?.Invoke(courseId, cancellationToken) ??
                   Task.FromResult(new CourseSourceStatusResult
                   {
                       CourseId = courseId,
                       RootPath = @"C:\Cursos\Local",
                       Status = CourseSourceStatus.Available,
                       Message = "Pasta disponível."
                   });
        }

        public Task<CourseSourceLocationValidationResult> ValidateLocationAsync(
            Guid courseId,
            string folderPath,
            CancellationToken cancellationToken = default)
        {
            ValidationCallCount++;
            return Task.FromResult(ValidationResult ?? new CourseSourceLocationValidationResult
            {
                CourseId = courseId,
                CandidateRootPath = folderPath,
                Compatibility = CourseSourceLocationCompatibility.ExactMatch,
                ErrorKind = CourseSourceLocationErrorKind.None
            });
        }

        public Task<CourseSourceLocationChangeResult> ChangeLocationAsync(
            ChangeCourseSourceLocationRequest request,
            CancellationToken cancellationToken = default)
        {
            ChangeRequests.Add(request);
            return Task.FromResult(ChangeResult ?? new CourseSourceLocationChangeResult
            {
                CourseId = request.CourseId,
                Status = CourseSourceLocationChangeStatus.Changed
            });
        }
    }

    private sealed class FakeContentSyncService : ICourseContentSyncService
    {
        public CourseContentSyncPreviewResult? PreviewResult { get; init; }
        public CourseContentSyncApplyResult? ApplyResult { get; init; }
        public Func<Guid, CancellationToken, Task<CourseContentSyncPreviewResult>>? PreviewHandler { get; init; }
        public Func<Guid, CancellationToken, Task<CourseContentSyncApplyResult>>? ApplyHandler { get; init; }
        public int PreviewCallCount { get; private set; }
        public int ApplyCallCount { get; private set; }

        public Task<CourseContentSyncPreviewResult> PreviewAsync(
            Guid courseId,
            CancellationToken cancellationToken = default)
        {
            PreviewCallCount++;
            return PreviewHandler?.Invoke(courseId, cancellationToken) ??
                   Task.FromResult(PreviewResult ?? new CourseContentSyncPreviewResult
            {
                CourseId = courseId,
                Status = CourseContentSyncPreviewStatus.NoChanges
            });
        }

        public Task<CourseContentSyncApplyResult> ApplyAsync(
            Guid courseId,
            CancellationToken cancellationToken = default)
        {
            ApplyCallCount++;
            return ApplyHandler?.Invoke(courseId, cancellationToken) ??
                   Task.FromResult(ApplyResult ?? new CourseContentSyncApplyResult
            {
                CourseId = courseId,
                Status = CourseContentSyncApplyStatus.NoChanges
            });
        }
    }
}
