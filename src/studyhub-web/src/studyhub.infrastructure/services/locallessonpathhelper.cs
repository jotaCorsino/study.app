namespace studyhub.infrastructure.services;

internal static class LocalLessonPathHelper
{
    public static bool TryResolvePhysicalPath(
        string? rootPath,
        string? relativeFilePath,
        out string resolvedFilePath)
    {
        resolvedFilePath = string.Empty;

        if (!TryNormalizeFullyQualifiedPath(rootPath, out var normalizedRootPath) ||
            !TryNormalizePortableRelativePath(relativeFilePath, out var normalizedRelativeFilePath))
        {
            return false;
        }

        try
        {
            var platformRelativeFilePath = normalizedRelativeFilePath.Replace(
                '/',
                Path.DirectorySeparatorChar);
            var candidate = Path.GetFullPath(Path.Combine(normalizedRootPath, platformRelativeFilePath));

            if (!TryCalculatePortableRelativePath(
                    normalizedRootPath,
                    candidate,
                    out _))
            {
                return false;
            }

            resolvedFilePath = candidate;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            return false;
        }
    }

    public static bool TryCalculatePortableRelativePath(
        string rootPath,
        string absoluteLessonPath,
        out string relativeFilePath)
    {
        relativeFilePath = string.Empty;

        if (!TryNormalizeFullyQualifiedPath(rootPath, out var normalizedRootPath) ||
            !TryNormalizeFullyQualifiedPath(absoluteLessonPath, out var normalizedLessonPath))
        {
            return false;
        }

        try
        {
            var candidate = Path.GetRelativePath(normalizedRootPath, normalizedLessonPath);
            return TryNormalizePortableRelativePath(candidate, out relativeFilePath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            return false;
        }
    }

    public static bool TryNormalizePortableRelativePath(string? path, out string relativeFilePath)
    {
        relativeFilePath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var candidate = path.Trim();
            var portableCandidate = candidate.Replace('\\', '/');
            var hasDrivePrefix = portableCandidate.Length >= 2 &&
                                 char.IsLetter(portableCandidate[0]) &&
                                 portableCandidate[1] == ':';

            if (portableCandidate == "." ||
                portableCandidate.StartsWith("/", StringComparison.Ordinal) ||
                hasDrivePrefix ||
                Path.IsPathRooted(candidate))
            {
                return false;
            }

            var segments = portableCandidate
                .Split('/', StringSplitOptions.RemoveEmptyEntries);

            if (segments.Length == 0 ||
                segments.Any(segment => segment is "." or "..") ||
                segments[0].StartsWith("..", StringComparison.Ordinal))
            {
                return false;
            }

            relativeFilePath = string.Join('/', segments);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            return false;
        }
    }

    public static bool TryNormalizeFullyQualifiedPath(string? path, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var candidate = path.Trim();
            if (!Path.IsPathFullyQualified(candidate))
            {
                return false;
            }

            normalizedPath = Path.GetFullPath(candidate);
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
        {
            return false;
        }
    }
}
