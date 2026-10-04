namespace Tideward.Core.Games;

public sealed record KuroAccountSnapshot(string Region, Dictionary<string, byte[]> Files)
{
    public static KuroAccountSnapshot Capture(string root, GameBiz region, IEnumerable<string> relativeFiles, Func<bool> isRunning)
    {
        EnsureStopped(isRunning); _ = KuroDistribution.ReadLocalVersion(root, region)
            ?? throw new InvalidOperationException("请先选择已验证区服的游戏安装目录。");
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        foreach (string relative in relativeFiles)
        {
            ValidateFile(relative);
            string path = GameFilePath.Resolve(root, relative);
            if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new IOException("账号缓存文件过大。");
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            byte[] bytes = new byte[checked((int)stream.Length)]; stream.ReadExactly(bytes);
            files.Add(relative, bytes);
        }
        if (files.Count == 0) throw new InvalidOperationException("请选择账号缓存文件。");
        EnsureStopped(isRunning);
        return new(region.Value, files);
    }

    public void Restore(string root, GameBiz region, Func<bool> isRunning)
    {
        EnsureStopped(isRunning);
        if (Region != region.Value || KuroDistribution.ReadLocalVersion(root, region) is null)
            throw new InvalidOperationException("账号快照与当前区服不匹配。");
        if (Files.Count == 0) throw new InvalidDataException("账号快照为空。");
        foreach (var (relative, bytes) in Files)
        { ValidateFile(relative); _ = GameFilePath.Resolve(root, relative); if (bytes.Length > 32 * 1024 * 1024) throw new InvalidDataException("账号快照过大。"); }
        var originals = new Dictionary<string, byte[]?>();
        var written = new List<string>();
        foreach (var (relative, bytes) in Files)
        {
            string path = GameFilePath.Resolve(root, relative);
            originals[path] = File.Exists(path) ? File.ReadAllBytes(path) : null;
        }
        EnsureStopped(isRunning);
        try
        {
            foreach (var (relative, bytes) in Files)
            {
                string path = GameFilePath.Resolve(root, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                string temp = path + ".tideward-new";
                try { File.WriteAllBytes(temp, bytes); File.Move(temp, path, true); written.Add(path); }
                finally { File.Delete(temp); }
            }
        }
        catch
        {
            foreach (string path in written.AsEnumerable().Reverse())
                if (originals[path] is { } bytes) File.WriteAllBytes(path, bytes); else File.Delete(path);
            throw;
        }
    }

    private static void EnsureStopped(Func<bool> isRunning)
    { if (isRunning()) throw new InvalidOperationException("请先退出鸣潮再保存或切换账号。"); }

    private static void ValidateFile(string relative)
    {
        if (!relative.Replace('\\', '/').StartsWith("Client/", StringComparison.OrdinalIgnoreCase)
            || !new[] { ".json", ".dat", ".ini", ".sav" }.Contains(Path.GetExtension(relative), StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("请选择 Client 目录内的账号缓存文件（JSON、DAT、INI 或 SAV）。数据库文件需要专门的事务适配。");
    }
}
