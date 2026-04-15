using studyhub.domain.Entities;

namespace studyhub.infrastructure.services;

internal static class CoursePresentationMergeHelper
{
    public static void MergeExistingPresentation(Course target, Course existing)
    {
        target.Title = ResolveDisplayValue(existing.RawTitle, existing.Title, target.RawTitle, target.Title);
        target.Description = ResolveDisplayValue(existing.RawDescription, existing.Description, target.RawDescription, target.Description);
        target.SourceMetadata = MergeSourceMetadata(target.SourceMetadata, existing.SourceMetadata);

        var existingModules = existing.Modules.ToDictionary(module => module.Id);
        foreach (var targetModule in target.Modules)
        {
            if (!existingModules.TryGetValue(targetModule.Id, out var existingModule))
            {
                continue;
            }

            targetModule.Title = ResolveDisplayValue(existingModule.RawTitle, existingModule.Title, targetModule.RawTitle, targetModule.Title);
            targetModule.Description = ResolveDisplayValue(existingModule.RawDescription, existingModule.Description, targetModule.RawDescription, targetModule.Description);

            var existingTopics = existingModule.Topics.ToDictionary(topic => topic.Id);
            foreach (var targetTopic in targetModule.Topics)
            {
                if (!existingTopics.TryGetValue(targetTopic.Id, out var existingTopic))
                {
                    continue;
                }

                targetTopic.Title = ResolveDisplayValue(existingTopic.RawTitle, existingTopic.Title, targetTopic.RawTitle, targetTopic.Title);
                targetTopic.Description = ResolveDisplayValue(existingTopic.RawDescription, existingTopic.Description, targetTopic.RawDescription, targetTopic.Description);

                var existingLessons = existingTopic.Lessons.ToDictionary(lesson => lesson.Id);
                foreach (var targetLesson in targetTopic.Lessons)
                {
                    if (!existingLessons.TryGetValue(targetLesson.Id, out var existingLesson))
                    {
                        continue;
                    }

                    targetLesson.Title = ResolveDisplayValue(existingLesson.RawTitle, existingLesson.Title, targetLesson.RawTitle, targetLesson.Title);
                    targetLesson.Description = ResolveDisplayValue(existingLesson.RawDescription, existingLesson.Description, targetLesson.RawDescription, targetLesson.Description);
                }
            }
        }
    }

    public static bool HasStructureChanged(Course current, Course existing)
    {
        if (!string.Equals(current.RawTitle, existing.RawTitle, StringComparison.Ordinal) ||
            current.Modules.Count != existing.Modules.Count)
        {
            return true;
        }

        var existingModules = existing.Modules.ToDictionary(module => module.Id);
        foreach (var currentModule in current.Modules)
        {
            if (!existingModules.TryGetValue(currentModule.Id, out var existingModule) ||
                !string.Equals(currentModule.RawTitle, existingModule.RawTitle, StringComparison.Ordinal) ||
                currentModule.Topics.Count != existingModule.Topics.Count)
            {
                return true;
            }

            var existingTopics = existingModule.Topics.ToDictionary(topic => topic.Id);
            foreach (var currentTopic in currentModule.Topics)
            {
                if (!existingTopics.TryGetValue(currentTopic.Id, out var existingTopic) ||
                    !string.Equals(currentTopic.RawTitle, existingTopic.RawTitle, StringComparison.Ordinal) ||
                    currentTopic.Lessons.Count != existingTopic.Lessons.Count)
                {
                    return true;
                }

                var existingLessons = existingTopic.Lessons.ToDictionary(lesson => lesson.Id);
                foreach (var currentLesson in currentTopic.Lessons)
                {
                    if (!existingLessons.TryGetValue(currentLesson.Id, out var existingLesson) ||
                        !string.Equals(currentLesson.RawTitle, existingLesson.RawTitle, StringComparison.Ordinal) ||
                        !string.Equals(currentLesson.LocalFilePath, existingLesson.LocalFilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private static CourseSourceMetadata MergeSourceMetadata(CourseSourceMetadata current, CourseSourceMetadata existing)
    {
        return new CourseSourceMetadata
        {
            RootPath = current.RootPath,
            ImportedAt = current.ImportedAt,
            LastEnrichedAt = existing.LastEnrichedAt,
            ScanVersion = current.ScanVersion,
            Provider = current.Provider,
            RequestedTopic = existing.RequestedTopic,
            RequestedObjective = existing.RequestedObjective,
            SearchQueries = existing.SearchQueries.ToList(),
            SourceUrls = existing.SourceUrls.ToList(),
            PlaylistIds = existing.PlaylistIds.ToList(),
            VideoIds = existing.VideoIds.ToList(),
            CompletedSteps = existing.CompletedSteps.ToList(),
            GenerationSummary = existing.GenerationSummary,
            CuratedSources = existing.CuratedSources.ToList()
        };
    }

    private static string ResolveDisplayValue(string existingRaw, string existingDisplay, string newRaw, string defaultDisplay)
    {
        if (!string.IsNullOrWhiteSpace(existingDisplay) &&
            string.Equals(existingRaw, newRaw, StringComparison.Ordinal))
        {
            return existingDisplay;
        }

        return defaultDisplay;
    }
}
