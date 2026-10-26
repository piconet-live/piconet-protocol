namespace Online.Protocol;

/// <summary>
/// A file written whole or not at all, the one way the launcher and the server write a file in place: its content
/// goes to a file beside it (its name and <see cref="Suffix"/>), which is then moved over it. A write that fails
/// raises what it raised, the file as it was, and leaves nothing beside it.
/// </summary>
public static class AtomicFile
{
    /// <summary>The file beside: the name and this.</summary>
    public const string Suffix = ".part";

    /// <summary>Writes the file through <paramref name="write"/>, which is handed the path of the file beside.</summary>
    public static void Write(string file, Action<string> write)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(write);
        var beside = file + Suffix;
        try
        {
            write(beside);
            File.Move(beside, file, overwrite: true);
        }
        catch
        {
            try
            {
                File.Delete(beside);
            }
            catch (Exception again) when (again is IOException or UnauthorizedAccessException)
            {
                // it could not be made at all, or cannot be removed: what the write raised is what counts
            }
            throw;
        }
    }

    public static void WriteAllBytes(string file, byte[] bytes) => Write(file, beside => File.WriteAllBytes(beside, bytes));

    public static void WriteAllText(string file, string text) => Write(file, beside => File.WriteAllText(beside, text));
}
