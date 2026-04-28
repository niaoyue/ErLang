using System.Text;
using System.Text.RegularExpressions;

namespace OwnDesk.Client;

internal static partial class ClientLog
{
    private const string LogFileName = "client.log";

    private static readonly Lock Gate = new();
    private static readonly Encoding Utf8NoBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);

    public static string LogPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OwnDesk",
            LogFileName);

    public static void Write(string message)
    {
        try
        {
            var directory = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var line = $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {Sanitize(message)}{Environment.NewLine}";
            lock (Gate)
            {
                File.AppendAllText(LogPath, line, Utf8NoBom);
            }
        }
        catch
        {
            // Logging must never break the client.
        }
    }

    public static void WriteException(string source, Exception exception)
    {
        Write($"{source}: {exception.GetType().Name}: {exception.Message}{Environment.NewLine}{exception.StackTrace}");
    }

    private static string Sanitize(string message)
    {
        return SensitiveValuePattern().Replace(message, "$1=<redacted>");
    }

    [GeneratedRegex("(?i)(token|password|session)[^\\r\\n]*")]
    private static partial Regex SensitiveValuePattern();
}
