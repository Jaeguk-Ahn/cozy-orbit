using System.Text.Json;
using System.Text.Json.Serialization;

namespace CozyOrbit.Core.Infra;

/// <summary>JSON 파일 읽기/쓰기. 쓰기는 임시 파일 + 교체로 원자적으로 수행해 손상 파일이 남지 않게 한다.</summary>
public static class JsonFile
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static T? Read<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            using var fs = File.OpenRead(path);
            return JsonSerializer.Deserialize<T>(fs, Options);
        }
        catch (Exception ex)
        {
            AppLog.Warn($"JSON 읽기 실패 {path}: {ex.Message}");
            return null;
        }
    }

    /// <param name="privateFile">true 면 macOS·Linux 에서 파일 권한을 0600(나만 읽기·쓰기)으로 — 토큰이 든 파일용</param>
    public static void WriteAtomic<T>(string path, T value, bool privateFile = false)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(value, Options);
        WriteAllBytesAtomic(path, bytes, privateFile);
    }

    public static void WriteAllBytesAtomic(string path, byte[] bytes, bool privateFile = false)
    {
        var dir = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, $".{Path.GetFileName(path)}.{Guid.NewGuid():N}.tmp");
        using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            fs.Write(bytes);
            // 교체 전에 내용을 디스크까지 — 안 하면 전원이 나갈 때 이름만 바뀌고 크기만 맞는 0 투성이 파일이 남는다
            fs.Flush(flushToDisk: true);
        }
        // 교체 전에 임시 파일에 권한을 걸어, 다른 사용자가 읽을 수 있는 순간이 없게
        if (privateFile && !OperatingSystem.IsWindows()) File.SetUnixFileMode(tmp, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        try
        {
            if (File.Exists(path)) File.Replace(tmp, path, null, ignoreMetadataErrors: true);
            else File.Move(tmp, path);
        }
        catch
        {
            // File.Replace 는 일부 파일시스템(네트워크 드라이브 등)에서 실패할 수 있다 → 덮어쓰기 이동으로 폴백
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmp)) { try { File.Delete(tmp); } catch { } }
        }
    }
}
