namespace ATEC.PM.Server.Services;

/// <summary>
/// Helper per gestire percorsi lunghi (>260 char).
/// In .NET 8 i path lunghi sono supportati nativamente su Windows 10+
/// se la policy è abilitata. Questo helper aggiunge il prefisso \\?\ come fallback.
/// </summary>
public static class LongPathHelper
{
    public static string Normalize(string path)
    {
        if (string.IsNullOrEmpty(path)) return path;
        string full = Path.GetFullPath(path);
        if (full.Length >= 240 && !full.StartsWith(@"\\?\"))
            return @"\\?\" + full;
        return full;
    }

    public static bool FileExists(string path) => File.Exists(Normalize(path));
    public static bool DirectoryExists(string path) => Directory.Exists(Normalize(path));
    public static void CreateDirectory(string path) => Directory.CreateDirectory(Normalize(path));

    public static void DeleteFile(string path) => File.Delete(Normalize(path));
    public static void DeleteDirectory(string path, bool recursive = true) => Directory.Delete(Normalize(path), recursive);

    public static void MoveFile(string source, string dest) => File.Move(Normalize(source), Normalize(dest));
    public static void MoveDirectory(string source, string dest) => Directory.Move(Normalize(source), Normalize(dest));

    public static byte[] ReadAllBytes(string path) => File.ReadAllBytes(Normalize(path));
    public static void WriteAllBytes(string path, byte[] data) => File.WriteAllBytes(Normalize(path), data);

    public static FileStream CreateFileStream(string path, FileMode mode) => new(Normalize(path), mode);

    public static string[] GetDirectories(string path) => Directory.GetDirectories(Normalize(path));
    public static string[] GetFiles(string path) => Directory.GetFiles(Normalize(path));
}