namespace NSSharp;

public static class XCFrameworkResolver
{
    /// <summary>
    /// Returns the names of all available slices in an .xcframework bundle.
    /// </summary>
    public static List<string> ListSlices(string xcframeworkPath)
    {
        if (!Directory.Exists(xcframeworkPath))
            throw new DirectoryNotFoundException($"XCFramework not found: {xcframeworkPath}");

        return Directory.GetDirectories(xcframeworkPath)
            .Select(Path.GetFileName)
            .Where(n => n != null && !n.StartsWith('.') && !n.StartsWith('_'))
            .Select(n => n!)
            .OrderBy(n => n)
            .ToList();
    }

    /// <summary>
    /// Discovers all public .h header files inside an .xcframework bundle.
    /// When <paramref name="slice"/> is specified, only that slice is used.
    /// Otherwise prefers the current platform slice, falling back to the first available.
    /// </summary>
    public static List<string> ResolveHeaders(string xcframeworkPath, string? slice = null)
    {
        if (!Directory.Exists(xcframeworkPath))
            throw new DirectoryNotFoundException($"XCFramework not found: {xcframeworkPath}");

        var slices = Directory.GetDirectories(xcframeworkPath)
            .Where(d => { var n = Path.GetFileName(d); return !n.StartsWith('.') && !n.StartsWith('_'); })
            .ToList();

        if (slices.Count == 0)
            throw new InvalidOperationException($"No slices found in {xcframeworkPath}");

        string preferred;
        if (slice != null)
        {
            preferred = slices.FirstOrDefault(s =>
                string.Equals(Path.GetFileName(s), slice, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"Slice '{slice}' not found. Available slices: {string.Join(", ", slices.Select(Path.GetFileName))}");
        }
        else
        {
            // Prefer macOS slice on macOS, iOS on iOS, etc.
            preferred = slices.FirstOrDefault(s =>
            {
                var name = Path.GetFileName(s)!.ToLowerInvariant();
                if (OperatingSystem.IsMacOS()) return name.Contains("macos") || name.Contains("macosx");
                if (OperatingSystem.IsIOS()) return name.Contains("ios") && !name.Contains("simulator");
                return false;
            }) ?? slices[0];
        }

        var headers = new List<string>();
        CollectHeaders(preferred, headers);

        // If no headers found in preferred (and no explicit slice), try all slices
        if (headers.Count == 0 && slice == null)
        {
            foreach (var s in slices)
            {
                CollectHeaders(s, headers);
                if (headers.Count > 0) break;
            }
        }

        return headers;
    }

    private static void CollectHeaders(string slicePath, List<string> headers)
    {
        // Look for Headers/ directory inside framework bundles or directly
        var headersDir = FindHeadersDirectory(slicePath);
        if (headersDir != null && Directory.Exists(headersDir))
        {
            headers.AddRange(Directory.GetFiles(headersDir, "*.h", SearchOption.AllDirectories));
        }
    }

    private static string? FindHeadersDirectory(string basePath)
    {
        // Direct Headers/ directory
        var direct = Path.Combine(basePath, "Headers");
        if (Directory.Exists(direct)) return direct;

        // Inside a .framework bundle
        var frameworks = Directory.GetDirectories(basePath, "*.framework", SearchOption.TopDirectoryOnly);
        foreach (var fw in frameworks)
        {
            var fwHeaders = Path.Combine(fw, "Headers");
            if (Directory.Exists(fwHeaders)) return fwHeaders;
        }

        return null;
    }
}
