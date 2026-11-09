/// <summary>Reads app source for the source-guard tests with line endings normalized to LF (Windows checkouts may use CRLF).</summary>
internal static class SourceText
{
    public static string Read(string path) => File.ReadAllText(path).Replace("\r\n", "\n");
}
