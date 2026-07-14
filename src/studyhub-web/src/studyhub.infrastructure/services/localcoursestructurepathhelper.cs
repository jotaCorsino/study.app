namespace studyhub.infrastructure.services;

internal static class LocalCourseStructurePathHelper
{
    private static StringComparer PathSegmentComparer => OperatingSystem.IsWindows()
        ? StringComparer.OrdinalIgnoreCase
        : StringComparer.Ordinal;

    public static bool TryNormalize(string? path, out string sourceRelativePath)
    {
        sourceRelativePath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var candidate = path.Trim();
        if (candidate == ".")
        {
            sourceRelativePath = ".";
            return true;
        }

        return LocalLessonPathHelper.TryNormalizePortableRelativePath(
            candidate,
            out sourceRelativePath);
    }

    public static bool TryCombine(
        string? parentRelativePath,
        string? childRelativePath,
        out string sourceRelativePath)
    {
        sourceRelativePath = string.Empty;
        if (!TryNormalize(parentRelativePath, out var normalizedParent) ||
            !TryNormalize(childRelativePath, out var normalizedChild))
        {
            return false;
        }

        if (normalizedParent == ".")
        {
            sourceRelativePath = normalizedChild;
            return true;
        }

        if (normalizedChild == ".")
        {
            sourceRelativePath = normalizedParent;
            return true;
        }

        return TryNormalize(
            $"{normalizedParent}/{normalizedChild}",
            out sourceRelativePath);
    }

    public static bool TryGetParentDirectoryFromLessonPath(
        string? relativeFilePath,
        out string sourceRelativePath)
    {
        sourceRelativePath = string.Empty;
        if (!LocalLessonPathHelper.TryNormalizePortableRelativePath(
                relativeFilePath,
                out var normalizedFilePath))
        {
            return false;
        }

        var lastSeparatorIndex = normalizedFilePath.LastIndexOf('/');
        sourceRelativePath = lastSeparatorIndex < 0
            ? "."
            : normalizedFilePath[..lastSeparatorIndex];
        return true;
    }

    public static bool TryGetCommonAncestor(
        IEnumerable<string> sourceRelativePaths,
        out string commonAncestor)
    {
        commonAncestor = string.Empty;
        if (sourceRelativePaths is null)
        {
            return false;
        }

        var normalizedPaths = new List<string>();
        foreach (var path in sourceRelativePaths)
        {
            if (!TryNormalize(path, out var normalizedPath))
            {
                return false;
            }

            normalizedPaths.Add(normalizedPath);
        }

        if (normalizedPaths.Count == 0)
        {
            return false;
        }

        if (normalizedPaths.Any(path => path == "."))
        {
            commonAncestor = ".";
            return true;
        }

        var firstSegments = normalizedPaths[0].Split('/');
        var commonSegmentCount = firstSegments.Length;

        foreach (var normalizedPath in normalizedPaths.Skip(1))
        {
            var segments = normalizedPath.Split('/');
            commonSegmentCount = Math.Min(commonSegmentCount, segments.Length);

            var index = 0;
            while (index < commonSegmentCount &&
                   PathSegmentComparer.Equals(firstSegments[index], segments[index]))
            {
                index++;
            }

            commonSegmentCount = index;
            if (commonSegmentCount == 0)
            {
                commonAncestor = ".";
                return true;
            }
        }

        commonAncestor = string.Join('/', firstSegments.Take(commonSegmentCount));
        return true;
    }

    public static bool TryMakeRelativeToParent(
        string? parentRelativePath,
        string? childRootRelativePath,
        out string childRelativePath)
    {
        childRelativePath = string.Empty;
        if (!TryNormalize(parentRelativePath, out var normalizedParent) ||
            !TryNormalize(childRootRelativePath, out var normalizedChild))
        {
            return false;
        }

        if (PathSegmentComparer.Equals(normalizedParent, normalizedChild))
        {
            childRelativePath = ".";
            return true;
        }

        if (normalizedParent == ".")
        {
            childRelativePath = normalizedChild;
            return true;
        }

        if (normalizedChild == ".")
        {
            return false;
        }

        var parentSegments = normalizedParent.Split('/');
        var childSegments = normalizedChild.Split('/');
        if (childSegments.Length <= parentSegments.Length)
        {
            return false;
        }

        for (var index = 0; index < parentSegments.Length; index++)
        {
            if (!PathSegmentComparer.Equals(parentSegments[index], childSegments[index]))
            {
                return false;
            }
        }

        childRelativePath = string.Join('/', childSegments.Skip(parentSegments.Length));
        return true;
    }
}
