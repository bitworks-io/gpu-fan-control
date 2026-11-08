namespace LightweightAmdGpuFanControl.Drivers;

/// <summary>Outcome of resolving a package file: either a <see cref="Path"/> or a <see cref="Reason"/> for refusal.</summary>
public sealed class InfPackageFileResult
{
    public string? Path { get; init; }
    public string? Reason { get; init; }
}

/// <summary>
/// Resolves where a file of a driver package lives, using only the package INF's own
/// [SourceDisksFiles(.amd64)] / [SourceDisksNames(.amd64)] entries. Pure string handling with
/// '\' separators (Windows paths), so it behaves identically on any host OS. Never throws.
/// </summary>
public static class InfPackageFile
{
    private const string IllegalPathChars = "<>\"/|?*";

    public static InfPackageFileResult Resolve(string storeInfPath, IReadOnlyList<string>? infLines, string fileName)
    {
        try
        {
            return ResolveCore(storeInfPath, infLines, fileName);
        }
        catch (Exception ex)
        {
            return Refuse("unexpected error resolving INF entry: " + ex.Message);
        }
    }

    private static InfPackageFileResult ResolveCore(string storeInfPath, IReadOnlyList<string>? infLines, string fileName)
    {
        if (infLines == null) return Refuse("INF content unavailable");
        if (string.IsNullOrEmpty(fileName) || fileName.IndexOf('\\') >= 0 || fileName.IndexOf('/') >= 0)
            return Refuse("invalid file name");
        if (string.IsNullOrEmpty(storeInfPath)) return Refuse("INF path unavailable");
        int slash = storeInfPath.LastIndexOf('\\');
        if (slash <= 0) return Refuse("INF path has no folder");
        string packageFolder = storeInfPath.Substring(0, slash);

        var sections = Parse(infLines);

        if (!TryGetSection(sections, "sourcedisksfiles.amd64", "sourcedisksfiles", out var files))
            return Refuse("INF has no SourceDisksFiles section");
        string[]? fileFields = null;
        // amd64 section wins; fall back to the plain section only if the amd64 one lacks the file.
        foreach (var name in new[] { "sourcedisksfiles.amd64", "sourcedisksfiles" })
        {
            if (sections.TryGetValue(name, out var sec) && sec.TryGetValue(fileName, out var f)) { fileFields = f; break; }
        }
        if (fileFields == null) return Refuse($"{fileName} is not listed in the INF SourceDisksFiles");
        string diskId = fileFields.Length > 0 ? fileFields[0] : "";
        string subdir = fileFields.Length > 1 ? fileFields[1] : "";

        string[]? nameFields = null;
        bool anyNames = false;
        foreach (var name in new[] { "sourcedisksnames.amd64", "sourcedisksnames" })
        {
            if (!sections.TryGetValue(name, out var sec)) continue;
            anyNames = true;
            if (sec.TryGetValue(diskId, out var n)) { nameFields = n; break; }
        }
        if (!anyNames) return Refuse("INF has no SourceDisksNames section");
        if (nameFields == null) return Refuse($"disk id '{diskId}' is not defined in the INF SourceDisksNames");
        string diskPath = nameFields.Length > 3 ? nameFields[3] : "";

        var segments = new List<string>();
        string? err = AddSegments(diskPath, "disk path", segments);
        if (err != null) return Refuse(err);
        err = AddSegments(subdir, "subdirectory", segments);
        if (err != null) return Refuse(err);

        var result = packageFolder;
        foreach (var s in segments) result += "\\" + s;
        return new InfPackageFileResult { Path = result + "\\" + fileName };
    }

    private static string? AddSegments(string value, string what, List<string> into)
    {
        if (value.Length == 0) return null;
        if (value.StartsWith("\\\\", StringComparison.Ordinal)) return $"{what} is a UNC path";
        if (value.IndexOf(':') >= 0) return $"{what} contains ':'";
        if (value.IndexOf('%') >= 0) return $"{what} contains an unexpanded %token%";
        foreach (char c in value)
        {
            if (c < 0x20 || c == 0x7f || IllegalPathChars.IndexOf(c) >= 0) return $"{what} contains an illegal character";
        }
        foreach (var seg in value.Split('\\'))
        {
            // "." is the INF convention for "the package folder" (the RDNA1/2 package ships ".\B026653") and cannot
            // leave it, so it is a no-op. ".." could leave the package folder and stays refused.
            if (seg.Length == 0 || seg == ".") continue;
            if (seg == "..") return $"{what} contains a parent-folder segment";
            into.Add(seg);
        }
        return null;
    }

    private static bool TryGetSection(Dictionary<string, Dictionary<string, string[]>> s, string a, string b,
        out Dictionary<string, string[]> found)
    {
        if (s.TryGetValue(a, out found!)) return true;
        return s.TryGetValue(b, out found!);
    }

    private static Dictionary<string, Dictionary<string, string[]>> Parse(IReadOnlyList<string> lines)
    {
        var result = new Dictionary<string, Dictionary<string, string[]>>(StringComparer.OrdinalIgnoreCase);
        Dictionary<string, string[]>? current = null;
        foreach (var raw in lines)
        {
            if (raw == null) continue;
            string line = StripComment(raw).Trim();
            if (line.Length == 0) continue;
            if (line[0] == '[')
            {
                int close = line.IndexOf(']');
                string name = (close > 0 ? line.Substring(1, close - 1) : line.Substring(1)).Trim();
                if (!result.TryGetValue(name, out current))
                {
                    current = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                    result[name] = current;
                }
                continue;
            }
            if (current == null) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string key = Unquote(line.Substring(0, eq).Trim());
            var fields = SplitFields(line.Substring(eq + 1));
            if (!current.ContainsKey(key)) current[key] = fields;
        }
        return result;
    }

    private static string StripComment(string line)
    {
        bool inQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] == '"') inQuotes = !inQuotes;
            else if (line[i] == ';' && !inQuotes) return line.Substring(0, i);
        }
        return line;
    }

    private static string[] SplitFields(string value)
    {
        var fields = new List<string>();
        var cur = new System.Text.StringBuilder();
        bool inQuotes = false;
        foreach (char c in value)
        {
            if (c == '"') { inQuotes = !inQuotes; cur.Append(c); }
            else if (c == ',' && !inQuotes) { fields.Add(Unquote(cur.ToString().Trim())); cur.Clear(); }
            else cur.Append(c);
        }
        fields.Add(Unquote(cur.ToString().Trim()));
        return fields.ToArray();
    }

    private static string Unquote(string s)
    {
        if (s.Length >= 2 && s[0] == '"' && s[s.Length - 1] == '"') return s.Substring(1, s.Length - 2).Trim();
        return s;
    }

    private static InfPackageFileResult Refuse(string reason) => new() { Reason = reason };
}
