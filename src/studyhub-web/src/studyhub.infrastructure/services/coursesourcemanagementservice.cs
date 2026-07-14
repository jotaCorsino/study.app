using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using studyhub.application.Contracts.CourseBuilding;
using studyhub.application.Contracts.CourseSourceManagement;
using studyhub.application.Contracts.LocalImport;
using studyhub.application.Interfaces;
using studyhub.domain.Entities;
using studyhub.infrastructure.persistence;
using studyhub.infrastructure.persistence.models;

namespace studyhub.infrastructure.services;

public sealed class CourseSourceManagementService(
    IDbContextFactory<StudyHubDbContext> contextFactory,
    ILocalFolderCourseBuilder localFolderCourseBuilder,
    ILogger<CourseSourceManagementService> logger) : ICourseSourceManagementService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private static readonly StringComparer PathComparer = OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    private readonly IDbContextFactory<StudyHubDbContext> _contextFactory = contextFactory;
    private readonly ILocalFolderCourseBuilder _localFolderCourseBuilder = localFolderCourseBuilder;
    private readonly ILogger<CourseSourceManagementService> _logger = logger;

    public async Task<CourseSourceLocationValidationResult> ValidateLocationAsync(
        Guid courseId,
        string folderPath,
        CancellationToken cancellationToken = default)
    {
        var outcome = await ValidateCoreAsync(courseId, folderPath, cancellationToken);
        return outcome.Result;
    }

    public async Task<CourseSourceLocationChangeResult> ChangeLocationAsync(
        ChangeCourseSourceLocationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validationOutcome = await ValidateCoreAsync(
            request.CourseId,
            request.FolderPath,
            cancellationToken);
        var validation = validationOutcome.Result;

        if (!validation.Success)
        {
            return CreateChangeResult(
                request.CourseId,
                CourseSourceLocationChangeStatus.ValidationFailed,
                validation.ErrorKind,
                validation.Message,
                validation);
        }

        var blockedResult = TryCreateBlockedResult(request, validation);
        if (blockedResult is not null)
        {
            return blockedResult;
        }

        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);

            var course = await LoadCourseAsync(context, request.CourseId, tracking: true, cancellationToken);
            if (course is null)
            {
                return CreateChangeResult(
                    request.CourseId,
                    CourseSourceLocationChangeStatus.ValidationFailed,
                    CourseSourceLocationErrorKind.CourseNotFound,
                    "O curso não foi encontrado.",
                    validation);
            }

            if (course.SourceType != CourseSourceType.LocalFolder)
            {
                return CreateChangeResult(
                    request.CourseId,
                    CourseSourceLocationChangeStatus.ValidationFailed,
                    CourseSourceLocationErrorKind.UnsupportedCourseSource,
                    "A origem desse curso não é uma pasta local.",
                    validation);
            }

            var currentRootPath = ResolveCurrentRootPath(course);
            var existingPaths = CollectExistingRelativePaths(course, currentRootPath);
            validation = Classify(
                course.Id,
                currentRootPath,
                validationOutcome.NormalizedCandidateRootPath,
                CountLessons(course),
                existingPaths,
                validationOutcome.CandidateRelativePaths);

            blockedResult = TryCreateBlockedResult(request, validation);
            if (blockedResult is not null)
            {
                return blockedResult;
            }

            if (!TryRebaseSourceMetadata(
                    course.SourceMetadataJson,
                    validationOutcome.NormalizedCandidateRootPath,
                    out var rebasedMetadataJson))
            {
                return CreateChangeResult(
                    request.CourseId,
                    CourseSourceLocationChangeStatus.Failed,
                    CourseSourceLocationErrorKind.PersistenceFailed,
                    "Os metadados da origem do curso estão inválidos e não puderam ser preservados com segurança.",
                    validation);
            }

            var snapshot = await context.CourseImportSnapshots
                .SingleOrDefaultAsync(record => record.CourseId == request.CourseId, cancellationToken);
            string? rebasedStructureJson = null;
            var snapshotRebaseStatus = snapshot is null
                ? SnapshotRebaseStatus.NotPresent
                : TryRebaseSnapshotStructure(
                    snapshot.StructureJson,
                    validationOutcome.NormalizedCandidateRootPath,
                    course,
                    currentRootPath,
                    out rebasedStructureJson);

            if (snapshotRebaseStatus == SnapshotRebaseStatus.IdentityMismatch)
            {
                _logger.LogError(
                    "The import manifest identities for course {CourseId} do not match its persisted tree. Relocation was blocked to preserve the course identity.",
                    request.CourseId);
                return CreateChangeResult(
                    request.CourseId,
                    CourseSourceLocationChangeStatus.Failed,
                    CourseSourceLocationErrorKind.PersistenceFailed,
                    "O snapshot do curso não corresponde à identidade persistida; a origem não foi alterada.",
                    validation);
            }

            var rebasedLessonPaths = course.Modules
                .SelectMany(module => module.Topics)
                .SelectMany(topic => topic.Lessons)
                .Select(lesson => new
                {
                    Lesson = lesson,
                    CanRebase = LocalLessonPathHelper.TryResolvePhysicalPath(
                        validationOutcome.NormalizedCandidateRootPath,
                        lesson.RelativeFilePath,
                        out var absolutePath),
                    AbsolutePath = absolutePath
                })
                .Where(item => item.CanRebase)
                .ToList();

            course.FolderPath = validationOutcome.NormalizedCandidateRootPath;
            course.SourceMetadataJson = rebasedMetadataJson;

            foreach (var item in rebasedLessonPaths)
            {
                item.Lesson.LocalFilePath = item.AbsolutePath;
                item.Lesson.FilePath = item.AbsolutePath;
            }

            if (snapshot is not null)
            {
                snapshot.RootFolderPath = validationOutcome.NormalizedCandidateRootPath;

                if (snapshotRebaseStatus == SnapshotRebaseStatus.Rebased)
                {
                    snapshot.StructureJson = rebasedStructureJson!;
                }
                else
                {
                    _logger.LogWarning(
                        "The import manifest for course {CourseId} is invalid. Its root record was updated, but the original manifest JSON was preserved.",
                        request.CourseId);
                }
            }

            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            var status = validation.IsCurrentLocation
                ? CourseSourceLocationChangeStatus.Unchanged
                : CourseSourceLocationChangeStatus.Changed;
            var message = validation.IsCurrentLocation
                ? "A pasta selecionada já era a origem atual; os dados físicos relacionados foram mantidos coerentes."
                : "A origem física do curso foi alterada sem substituir o conteúdo persistido.";

            return CreateChangeResult(
                request.CourseId,
                status,
                CourseSourceLocationErrorKind.None,
                message,
                validation);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            _logger.LogError(ex, "Could not persist the source relocation for course {CourseId}.", request.CourseId);
            return CreateChangeResult(
                request.CourseId,
                CourseSourceLocationChangeStatus.Failed,
                CourseSourceLocationErrorKind.PersistenceFailed,
                "Não foi possível salvar a nova origem do curso.",
                validation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while relocating the source for course {CourseId}.", request.CourseId);
            return CreateChangeResult(
                request.CourseId,
                CourseSourceLocationChangeStatus.Failed,
                CourseSourceLocationErrorKind.Unexpected,
                "Ocorreu um erro inesperado ao alterar a origem do curso.",
                validation);
        }
    }

    private async Task<ValidationOutcome> ValidateCoreAsync(
        Guid courseId,
        string folderPath,
        CancellationToken cancellationToken)
    {
        if (!TryNormalizeRootPath(folderPath, out var candidateRootPath))
        {
            return ValidationOutcome.FromError(
                courseId,
                candidateRootPath,
                CourseSourceLocationErrorKind.InvalidFolder,
                "Selecione uma pasta local válida e existente.");
        }

        try
        {
            if (!File.GetAttributes(candidateRootPath).HasFlag(FileAttributes.Directory))
            {
                return ValidationOutcome.FromError(
                    courseId,
                    candidateRootPath,
                    CourseSourceLocationErrorKind.InvalidFolder,
                    "Selecione uma pasta local válida e existente.");
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            _logger.LogWarning(ex, "Access denied while checking candidate source {CandidateRootPath}.", candidateRootPath);
            return ValidationOutcome.FromError(
                courseId,
                candidateRootPath,
                CourseSourceLocationErrorKind.AccessDenied,
                "O acesso à pasta selecionada foi negado.");
        }
        catch (Exception ex) when (ex is DirectoryNotFoundException or FileNotFoundException)
        {
            return ValidationOutcome.FromError(
                courseId,
                candidateRootPath,
                CourseSourceLocationErrorKind.InvalidFolder,
                "Selecione uma pasta local válida e existente.");
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "I/O error while checking candidate source {CandidateRootPath}.", candidateRootPath);
            return ValidationOutcome.FromError(
                courseId,
                candidateRootPath,
                CourseSourceLocationErrorKind.ScanFailed,
                "Não foi possível acessar a pasta selecionada.");
        }

        CourseRecord? course;
        try
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            course = await LoadCourseAsync(context, courseId, tracking: false, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not load course {CourseId} while validating its source.", courseId);
            return ValidationOutcome.FromError(
                courseId,
                candidateRootPath,
                CourseSourceLocationErrorKind.Unexpected,
                "Não foi possível carregar o curso para validar a nova origem.");
        }

        if (course is null)
        {
            return ValidationOutcome.FromError(
                courseId,
                candidateRootPath,
                CourseSourceLocationErrorKind.CourseNotFound,
                "O curso não foi encontrado.");
        }

        var currentRootPath = ResolveCurrentRootPath(course);
        if (course.SourceType != CourseSourceType.LocalFolder)
        {
            return ValidationOutcome.FromError(
                courseId,
                candidateRootPath,
                CourseSourceLocationErrorKind.UnsupportedCourseSource,
                "A origem desse curso não é uma pasta local.",
                currentRootPath);
        }

        var existingPaths = CollectExistingRelativePaths(course, currentRootPath);
        DetectedCourseStructure candidateStructure;

        try
        {
            var buildResult = await _localFolderCourseBuilder.BuildAsync(
                new LocalFolderCourseBuildRequest { FolderPath = candidateRootPath },
                cancellationToken);
            candidateStructure = buildResult.DetectedStructure;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            _logger.LogWarning(ex, "Access denied while scanning candidate source {CandidateRootPath}.", candidateRootPath);
            return ValidationOutcome.FromError(
                courseId,
                candidateRootPath,
                CourseSourceLocationErrorKind.AccessDenied,
                "O acesso à pasta selecionada foi negado.",
                currentRootPath,
                CountLessons(course),
                existingPaths.Count);
        }
        catch (DirectoryNotFoundException ex)
        {
            _logger.LogWarning(ex, "Candidate source {CandidateRootPath} was not found during its scan.", candidateRootPath);
            return ValidationOutcome.FromError(
                courseId,
                candidateRootPath,
                CourseSourceLocationErrorKind.InvalidFolder,
                "A pasta selecionada não está mais disponível.",
                currentRootPath,
                CountLessons(course),
                existingPaths.Count);
        }
        catch (IOException ex)
        {
            _logger.LogWarning(ex, "I/O error while scanning candidate source {CandidateRootPath}.", candidateRootPath);
            return ValidationOutcome.FromError(
                courseId,
                candidateRootPath,
                CourseSourceLocationErrorKind.ScanFailed,
                "Não foi possível ler o conteúdo da pasta selecionada.",
                currentRootPath,
                CountLessons(course),
                existingPaths.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error while scanning candidate source {CandidateRootPath}.", candidateRootPath);
            return ValidationOutcome.FromError(
                courseId,
                candidateRootPath,
                CourseSourceLocationErrorKind.Unexpected,
                "Ocorreu um erro inesperado ao verificar a pasta selecionada.",
                currentRootPath,
                CountLessons(course),
                existingPaths.Count);
        }

        var candidatePaths = CollectCandidateRelativePaths(candidateStructure);
        if (candidatePaths.Count == 0)
        {
            return ValidationOutcome.FromError(
                courseId,
                candidateRootPath,
                CourseSourceLocationErrorKind.NoVideosFound,
                "A pasta selecionada não contém vídeos reconhecidos.",
                currentRootPath,
                CountLessons(course),
                existingPaths.Count);
        }

        return new ValidationOutcome(
            Classify(
                courseId,
                currentRootPath,
                candidateRootPath,
                CountLessons(course),
                existingPaths,
                candidatePaths),
            candidateRootPath,
            candidatePaths);
    }

    private static async Task<CourseRecord?> LoadCourseAsync(
        StudyHubDbContext context,
        Guid courseId,
        bool tracking,
        CancellationToken cancellationToken)
    {
        var query = context.Courses
            .Include(record => record.Modules)
                .ThenInclude(record => record.Topics)
                    .ThenInclude(record => record.Lessons)
            .Where(record => record.Id == courseId);

        return tracking
            ? await query.SingleOrDefaultAsync(cancellationToken)
            : await query.AsNoTracking().SingleOrDefaultAsync(cancellationToken);
    }

    private static HashSet<string> CollectExistingRelativePaths(CourseRecord course, string currentRootPath)
    {
        var relativePaths = new HashSet<string>(PathComparer);

        foreach (var lesson in course.Modules
                     .SelectMany(module => module.Topics)
                     .SelectMany(topic => topic.Lessons))
        {
            if (TryGetComparableRelativePath(lesson, currentRootPath, out var relativePath))
            {
                relativePaths.Add(relativePath);
            }
        }

        return relativePaths;
    }

    private static bool TryGetComparableRelativePath(
        LessonRecord lesson,
        string currentRootPath,
        out string relativePath)
        => LocalLessonPathHelper.TryNormalizePortableRelativePath(
               lesson.RelativeFilePath,
               out relativePath) ||
           LocalLessonPathHelper.TryCalculatePortableRelativePath(
               currentRootPath,
               lesson.LocalFilePath,
               out relativePath) ||
           LocalLessonPathHelper.TryCalculatePortableRelativePath(
               currentRootPath,
               lesson.FilePath,
               out relativePath);

    private static HashSet<string> CollectCandidateRelativePaths(DetectedCourseStructure structure)
    {
        var relativePaths = new HashSet<string>(PathComparer);

        foreach (var lesson in structure.Modules
                     .SelectMany(module => module.Topics)
                     .SelectMany(topic => topic.Lessons))
        {
            if (LocalLessonPathHelper.TryNormalizePortableRelativePath(
                    lesson.RelativePath,
                    out var relativePath))
            {
                relativePaths.Add(relativePath);
            }
        }

        return relativePaths;
    }

    private static CourseSourceLocationValidationResult Classify(
        Guid courseId,
        string currentRootPath,
        string candidateRootPath,
        int existingLessonCount,
        HashSet<string> existingPaths,
        HashSet<string> candidatePaths)
    {
        var matchedCount = existingPaths.Count(path => candidatePaths.Contains(path));
        var missingCount = existingPaths.Count - matchedCount;
        var newCount = candidatePaths.Count(path => !existingPaths.Contains(path));
        var compatibility = CourseSourceLocationCompatibility.NotEvaluated;
        var message = string.Empty;

        if (existingPaths.Count == 0)
        {
            compatibility = CourseSourceLocationCompatibility.InsufficientReferenceData;
            message = "Não há caminhos relativos confiáveis suficientes para reconhecer este curso.";
        }
        else if (matchedCount == 0)
        {
            compatibility = CourseSourceLocationCompatibility.Incompatible;
            message = "Nenhuma aula conhecida foi encontrada na pasta selecionada.";
        }
        else if (missingCount == 0 && newCount == 0)
        {
            compatibility = CourseSourceLocationCompatibility.ExactMatch;
            message = "A pasta contém exatamente todas as aulas conhecidas do curso.";
        }
        else if (missingCount == 0)
        {
            compatibility = CourseSourceLocationCompatibility.CompatibleWithNewContent;
            message = "Todas as aulas conhecidas foram encontradas e há conteúdo novo ainda não sincronizado.";
        }
        else
        {
            compatibility = CourseSourceLocationCompatibility.PartialMatch;
            message = "Apenas parte das aulas conhecidas foi encontrada; a alteração exige confirmação explícita.";
        }

        return new CourseSourceLocationValidationResult
        {
            CourseId = courseId,
            CurrentRootPath = currentRootPath,
            CandidateRootPath = candidateRootPath,
            ExistingLessonCount = existingLessonCount,
            ExistingComparableLessonCount = existingPaths.Count,
            CandidateLessonCount = candidatePaths.Count,
            MatchedExistingLessonCount = matchedCount,
            MissingExistingLessonCount = missingCount,
            NewCandidateLessonCount = newCount,
            Compatibility = compatibility,
            ErrorKind = CourseSourceLocationErrorKind.None,
            Message = message,
            IsCurrentLocation = AreSamePath(currentRootPath, candidateRootPath)
        };
    }

    private static CourseSourceLocationChangeResult? TryCreateBlockedResult(
        ChangeCourseSourceLocationRequest request,
        CourseSourceLocationValidationResult validation)
    {
        if (validation.Compatibility == CourseSourceLocationCompatibility.PartialMatch &&
            !request.ConfirmPartialMatch)
        {
            return CreateChangeResult(
                request.CourseId,
                CourseSourceLocationChangeStatus.PartialConfirmationRequired,
                CourseSourceLocationErrorKind.None,
                "Confirme explicitamente a correspondência parcial antes de alterar a origem.",
                validation);
        }

        if (validation.Compatibility is CourseSourceLocationCompatibility.Incompatible or
            CourseSourceLocationCompatibility.InsufficientReferenceData or
            CourseSourceLocationCompatibility.NotEvaluated)
        {
            return CreateChangeResult(
                request.CourseId,
                CourseSourceLocationChangeStatus.Blocked,
                CourseSourceLocationErrorKind.None,
                validation.Message,
                validation);
        }

        return null;
    }

    private static CourseSourceLocationChangeResult CreateChangeResult(
        Guid courseId,
        CourseSourceLocationChangeStatus status,
        CourseSourceLocationErrorKind errorKind,
        string message,
        CourseSourceLocationValidationResult validation)
        => new()
        {
            CourseId = courseId,
            Status = status,
            ErrorKind = errorKind,
            Message = message,
            Validation = validation
        };

    private static string ResolveCurrentRootPath(CourseRecord course)
    {
        if (TryReadMetadataRootPath(course.SourceMetadataJson, out var metadataRootPath) &&
            TryNormalizeRootPath(metadataRootPath, out var normalizedMetadataRootPath))
        {
            return normalizedMetadataRootPath;
        }

        return TryNormalizeRootPath(course.FolderPath, out var normalizedFolderPath)
            ? normalizedFolderPath
            : course.FolderPath;
    }

    private static bool TryReadMetadataRootPath(string metadataJson, out string rootPath)
    {
        rootPath = string.Empty;

        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return false;
        }

        try
        {
            var root = JsonNode.Parse(metadataJson) as JsonObject;
            var propertyName = FindPropertyName(root, "rootPath");
            rootPath = propertyName is null
                ? string.Empty
                : root![propertyName]?.GetValue<string>() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(rootPath);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryRebaseSourceMetadata(
        string metadataJson,
        string newRootPath,
        out string rebasedMetadataJson)
    {
        rebasedMetadataJson = string.Empty;

        try
        {
            JsonObject root;
            if (string.IsNullOrWhiteSpace(metadataJson))
            {
                root = [];
            }
            else if (JsonNode.Parse(metadataJson) is JsonObject parsedRoot)
            {
                root = parsedRoot;
            }
            else
            {
                return false;
            }

            var propertyName = FindPropertyName(root, "rootPath") ?? "rootPath";
            root[propertyName] = newRootPath;
            rebasedMetadataJson = root.ToJsonString(JsonOptions);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static SnapshotRebaseStatus TryRebaseSnapshotStructure(
        string structureJson,
        string newRootPath,
        CourseRecord course,
        string currentRootPath,
        out string rebasedStructureJson)
    {
        rebasedStructureJson = structureJson;

        try
        {
            if (string.IsNullOrWhiteSpace(structureJson) ||
                JsonSerializer.Deserialize<DetectedCourseStructure>(structureJson, JsonOptions) is not { } manifest ||
                JsonNode.Parse(structureJson) is not JsonObject root)
            {
                return SnapshotRebaseStatus.Invalid;
            }

            if (!LocalCourseManifestValidator.HasUsableStructure(manifest))
            {
                return SnapshotRebaseStatus.Invalid;
            }

            if (!HasMatchingManifestIdentities(manifest, course, currentRootPath))
            {
                return SnapshotRebaseStatus.IdentityMismatch;
            }

            SetProperty(root, "rootFolderPath", newRootPath);
            var rootFolderName = Path.GetFileName(Path.TrimEndingDirectorySeparator(newRootPath));
            if (!string.IsNullOrWhiteSpace(rootFolderName))
            {
                SetProperty(root, "rootFolderName", rootFolderName);
            }

            RebaseAbsoluteLessonPaths(root, newRootPath);
            rebasedStructureJson = root.ToJsonString(JsonOptions);
            return SnapshotRebaseStatus.Rebased;
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or ArgumentException)
        {
            return SnapshotRebaseStatus.Invalid;
        }
    }

    private static bool HasMatchingManifestIdentities(
        DetectedCourseStructure manifest,
        CourseRecord course,
        string currentRootPath)
    {
        if (manifest.CourseId != course.Id)
        {
            return false;
        }

        var persistedModules = course.Modules.Select(module => module.Id).ToHashSet();
        var persistedTopics = course.Modules
            .SelectMany(module => module.Topics.Select(topic => new
            {
                TopicId = topic.Id,
                ModuleId = module.Id
            }))
            .ToDictionary(item => item.TopicId, item => item.ModuleId);
        var persistedLessons = course.Modules
            .SelectMany(module => module.Topics)
            .SelectMany(topic => topic.Lessons.Select(lesson => new
            {
                Lesson = lesson,
                TopicId = topic.Id
            }))
            .ToDictionary(
                item => item.Lesson.Id,
                item => new PersistedLessonIdentity(item.TopicId, item.Lesson));
        var manifestModules = manifest.Modules.Select(module => module.ModuleId).ToHashSet();
        var manifestTopics = manifest.Modules
            .SelectMany(module => module.Topics.Select(topic => new { topic.TopicId, module.ModuleId }))
            .ToDictionary(item => item.TopicId, item => item.ModuleId);
        var manifestLessons = manifest.Modules
            .SelectMany(module => module.Topics)
            .SelectMany(topic => topic.Lessons.Select(lesson => new { Lesson = lesson, topic.TopicId }))
            .ToDictionary(
                item => item.Lesson.LessonId,
                item => new ManifestLessonIdentity(item.TopicId, item.Lesson.RelativePath));

        if (!persistedModules.SetEquals(manifestModules) ||
            persistedTopics.Count != manifestTopics.Count ||
            persistedLessons.Count != manifestLessons.Count ||
            persistedTopics.Any(item =>
                !manifestTopics.TryGetValue(item.Key, out var moduleId) ||
                moduleId != item.Value))
        {
            return false;
        }

        foreach (var (lessonId, persistedLesson) in persistedLessons)
        {
            if (!manifestLessons.TryGetValue(lessonId, out var manifestLesson) ||
                manifestLesson.TopicId != persistedLesson.TopicId)
            {
                return false;
            }

            if (TryGetComparableRelativePath(
                    persistedLesson.Lesson,
                    currentRootPath,
                    out var persistedRelativePath) &&
                (!LocalLessonPathHelper.TryNormalizePortableRelativePath(
                     manifestLesson.RelativePath,
                     out var manifestRelativePath) ||
                 !PathComparer.Equals(persistedRelativePath, manifestRelativePath)))
            {
                return false;
            }
        }

        return true;
    }

    private static void RebaseAbsoluteLessonPaths(JsonNode? node, string newRootPath)
    {
        if (node is JsonObject jsonObject)
        {
            var lessonIdPropertyName = FindPropertyName(jsonObject, "lessonId");
            var relativePropertyName = FindPropertyName(jsonObject, "relativePath");

            if (lessonIdPropertyName is not null && relativePropertyName is not null)
            {
                string? relativePath = null;
                try
                {
                    relativePath = jsonObject[relativePropertyName]?.GetValue<string>();
                }
                catch (InvalidOperationException)
                {
                    // Preserve malformed entries and continue updating the rest of the manifest.
                }

                if (LocalLessonPathHelper.TryResolvePhysicalPath(
                        newRootPath,
                        relativePath,
                        out var absolutePath))
                {
                    var absolutePropertyName = FindPropertyName(jsonObject, "absolutePath") ?? "absolutePath";
                    jsonObject[absolutePropertyName] = absolutePath;
                }
            }

            foreach (var child in jsonObject.Select(property => property.Value).ToList())
            {
                RebaseAbsoluteLessonPaths(child, newRootPath);
            }
        }
        else if (node is JsonArray jsonArray)
        {
            foreach (var child in jsonArray.ToList())
            {
                RebaseAbsoluteLessonPaths(child, newRootPath);
            }
        }
    }

    private static void SetProperty(JsonObject jsonObject, string expectedName, string value)
    {
        var propertyName = FindPropertyName(jsonObject, expectedName) ?? expectedName;
        jsonObject[propertyName] = value;
    }

    private static string? FindPropertyName(JsonObject? jsonObject, string expectedName)
        => jsonObject?
            .Select(property => property.Key)
            .FirstOrDefault(name => string.Equals(name, expectedName, StringComparison.OrdinalIgnoreCase));

    private static bool TryNormalizeRootPath(string? path, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (!LocalLessonPathHelper.TryNormalizeFullyQualifiedPath(path, out var fullyQualifiedPath))
        {
            return false;
        }

        normalizedPath = Path.TrimEndingDirectorySeparator(fullyQualifiedPath);
        return true;
    }

    private static bool AreSamePath(string firstPath, string secondPath)
        => TryNormalizeRootPath(firstPath, out var normalizedFirstPath) &&
           TryNormalizeRootPath(secondPath, out var normalizedSecondPath) &&
           PathComparer.Equals(normalizedFirstPath, normalizedSecondPath);

    private static int CountLessons(CourseRecord course)
        => course.Modules
            .SelectMany(module => module.Topics)
            .Sum(topic => topic.Lessons.Count);

    private enum SnapshotRebaseStatus
    {
        NotPresent = 0,
        Rebased = 1,
        Invalid = 2,
        IdentityMismatch = 3
    }

    private sealed record PersistedLessonIdentity(Guid TopicId, LessonRecord Lesson);

    private sealed record ManifestLessonIdentity(Guid TopicId, string RelativePath);

    private sealed record ValidationOutcome(
        CourseSourceLocationValidationResult Result,
        string NormalizedCandidateRootPath,
        HashSet<string> CandidateRelativePaths)
    {
        public static ValidationOutcome FromError(
            Guid courseId,
            string candidateRootPath,
            CourseSourceLocationErrorKind errorKind,
            string message,
            string currentRootPath = "",
            int existingLessonCount = 0,
            int existingComparableLessonCount = 0)
            => new(
                new CourseSourceLocationValidationResult
                {
                    CourseId = courseId,
                    CurrentRootPath = currentRootPath,
                    CandidateRootPath = candidateRootPath,
                    ExistingLessonCount = existingLessonCount,
                    ExistingComparableLessonCount = existingComparableLessonCount,
                    Compatibility = CourseSourceLocationCompatibility.NotEvaluated,
                    ErrorKind = errorKind,
                    Message = message,
                    IsCurrentLocation = AreSamePath(currentRootPath, candidateRootPath)
                },
                candidateRootPath,
                new HashSet<string>(PathComparer));
    }
}
