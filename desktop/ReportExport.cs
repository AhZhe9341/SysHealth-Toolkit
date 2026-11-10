using System.IO;
using System.Text.Json;

namespace SysHealth;

internal static class ReportExport
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static string Serialize(PrintReport report) => JsonSerializer.Serialize(report, Options);

    public static void Write(string path, PrintReport report)
    {
        var destination = Path.GetFullPath(path);
        var temporary = Path.Combine(Path.GetDirectoryName(destination)!, $".syshealth-{Guid.NewGuid():N}.tmp");
        var created = false;
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew))
            {
                created = true;
                using var writer = new StreamWriter(stream);
                writer.Write(Serialize(report));
            }
            File.Move(temporary, destination, overwrite: true);
            created = false;
        }
        finally
        {
            if (created)
            {
                try { File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }
}
