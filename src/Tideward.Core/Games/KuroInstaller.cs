using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Tideward.Core.Games;

public sealed record KuroInstallProgress(string Phase, long Finished, long Total);
public delegate Task KuroPatchApplier(string source, string patch, string output, KuroPatchGroup group, CancellationToken token);

public sealed class KuroInstaller(HttpClient client, KuroPatchApplier? applyPatch = null, Func<int, CancellationToken, Task>? throttle = null, Action<int>? downloaded = null)
{
    public async Task InstallAsync(string root, GameBiz region, KuroRelease release, KuroManifest full,
        KuroPackage? patchPackage = null, KuroManifest? patch = null, bool predownload = false,
        IProgress<KuroInstallProgress>? progress = null, CancellationToken token = default)
    {
        _ = KuroDistribution.AppId(region);
        string work = GameFilePath.Resolve(root, ".tideward-install");
        Directory.CreateDirectory(work);
        using var taskLock = new FileStream(Path.Combine(work, "lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        Recover(root);
        var local = KuroDistribution.ReadLocalVersion(root, region);
        if (patchPackage is not null && local?.Version != patchPackage.Version)
            throw new InvalidDataException("Delta source version does not match installation.");
        if (full.Resource.Count == 0 || !Version.TryParse(release.Config.Version, out _))
            throw new InvalidDataException("Incomplete release.");
        var files = full.Resource.ToDictionary(x => x.Dest, StringComparer.OrdinalIgnoreCase);
        foreach (var file in files.Values) Validate(root, file);
        var deletes = patch?.DeleteFiles.Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
        foreach (var file in deletes) { ValidateDest(root, file); if (files.ContainsKey(file)) throw new InvalidDataException("Deletion conflicts with target manifest."); }
        string stage = Path.Combine(work, "stage", release.Config.Version);
        Directory.CreateDirectory(stage);
        var pending = new List<KuroFile>();
        long total = full.Resource.Sum(x => x.Size), finished = 0;
        foreach (var file in files.Values)
        {
            token.ThrowIfCancellationRequested();
            if (!await MatchesAsync(GameFilePath.Resolve(root, file.Dest), file, token)) pending.Add(file);
            finished += file.Size;
            progress?.Report(new("Checking", finished, total));
        }
        total = pending.Sum(x => x.Size); finished = 0;
        if (patch is not null && applyPatch is not null)
        {
            foreach (var group in patch.GroupInfos)
            {
                if (group.DstFiles.Count == 0 || !group.DstFiles.Any(x => pending.Any(p => p.Dest.Equals(x.Dest, StringComparison.OrdinalIgnoreCase)))) continue;
                foreach (var file in group.SrcFiles) Validate(root, file);
                foreach (var file in group.DstFiles)
                {
                    Validate(root, file);
                    if (!files.TryGetValue(file.Dest, out var target) || target.Size != file.Size || NormalizeMd5(target.Md5) != NormalizeMd5(file.Md5))
                        throw new InvalidDataException("Patch destination differs from full manifest.");
                }
                bool sourceMatches = true;
                foreach (var file in group.SrcFiles) sourceMatches &= await MatchesAsync(GameFilePath.Resolve(root, file.Dest), file, token);
                if (!sourceMatches) continue;
                var payload = patch.Resource.SingleOrDefault(x => x.Dest == group.Dest)
                    ?? throw new InvalidDataException("Missing patch payload.");
                Validate(root, payload);
                var patchPath = GameFilePath.Resolve(Path.Combine(work, "patches", release.Config.Version), payload.Dest);
                await DownloadFromReleaseAsync(release, (payload.FromFolder ?? patchPackage!.BaseUrl).TrimEnd('/') + "/" + payload.Dest, patchPath, payload, token);
                progress?.Report(new("Patching", finished, total));
                await applyPatch(root, patchPath, stage, group, token);
                foreach (var file in group.DstFiles)
                    if (!await MatchesAsync(GameFilePath.Resolve(stage, file.Dest), file, token)) throw new InvalidDataException("Patched output checksum mismatch.");
            }
        }
        foreach (var file in pending)
        {
            string staged = GameFilePath.Resolve(stage, file.Dest);
            if (!await MatchesAsync(staged, file, token))
                await DownloadFromReleaseAsync(release, (file.FromFolder ?? release.Config.BaseUrl).TrimEnd('/') + "/" + file.Dest, staged, file, token,
                    value => progress?.Report(new("Downloading", finished + value, total)));
            finished += file.Size;
            progress?.Report(new("Downloading", finished, total));
        }
        if (predownload) return;
        token.ThrowIfCancellationRequested();
        var version = new KuroLocalVersion(release.Config.Version, KuroDistribution.AppId(region));
        await File.WriteAllTextAsync(Path.Combine(stage, "launcherDownloadConfig.json"), JsonSerializer.Serialize(version, KuroDistribution.JsonOptions), token);
        var changes = pending.Select(x => x.Dest).Concat(deletes).Append("launcherDownloadConfig.json")
            .Select(x => new Change(x, File.Exists(GameFilePath.Resolve(root, x)), deletes.Contains(x, StringComparer.OrdinalIgnoreCase))).ToList();
        foreach (var change in changes)
        {
            string backup = GameFilePath.Resolve(Path.Combine(work, "backup"), change.Path);
            if (File.Exists(backup)) File.Delete(backup);
        }
        var journal = new Journal(release.Config.Version, changes);
        string journalPath = Path.Combine(work, "journal.json");
        File.WriteAllText(journalPath + ".tmp", JsonSerializer.Serialize(journal));
        File.Move(journalPath + ".tmp", journalPath, true);
        try
        {
            progress?.Report(new("Committing", 0, changes.Count));
            foreach (var change in changes)
            {
                string dest = GameFilePath.Resolve(root, change.Path);
                string backup = GameFilePath.Resolve(Path.Combine(work, "backup"), change.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                if (change.HadOriginal) File.Move(dest, backup, true);
                if (!change.Delete) File.Move(GameFilePath.Resolve(stage, change.Path), dest, true);
            }
            File.Delete(journalPath);
        }
        catch { Recover(root); throw; }
        foreach (var change in changes)
        {
            string backup = GameFilePath.Resolve(Path.Combine(work, "backup"), change.Path);
            if (File.Exists(backup)) File.Delete(backup);
        }
        progress?.Report(new("Finished", total, total));
    }

    public static void Recover(string root)
    {
        string work = GameFilePath.Resolve(root, ".tideward-install");
        string path = GameFilePath.Resolve(work, "journal.json");
        if (!File.Exists(path)) return;
        var journal = JsonSerializer.Deserialize<Journal>(File.ReadAllText(path)) ?? throw new InvalidDataException("Invalid installation journal.");
        if (!Version.TryParse(journal.Version, out _)) throw new InvalidDataException("Invalid journal version.");
        foreach (var change in journal.Changes.AsEnumerable().Reverse())
        {
            string dest = GameFilePath.Resolve(root, change.Path);
            string backup = GameFilePath.Resolve(Path.Combine(work, "backup"), change.Path);
            string staged = GameFilePath.Resolve(Path.Combine(work, "stage", journal.Version), change.Path);
            if (File.Exists(backup)) File.Move(backup, dest, true);
            else if (!change.HadOriginal && !change.Delete && !File.Exists(staged)) File.Delete(dest);
        }
        File.Delete(path);
    }

    private async Task DownloadFromReleaseAsync(KuroRelease release, string relative, string destination, KuroFile file,
        CancellationToken token, Action<long>? progress = null)
    {
        var mirrors = release.CdnList.Where(x => x.Weight > 0).OrderByDescending(x => x.Weight).DistinctBy(x => x.Url).ToList();
        Exception? last = null;
        for (int attempt = 0; attempt < 2; attempt++)
        foreach (var mirror in mirrors)
        {
            token.ThrowIfCancellationRequested();
            var url = KuroDistribution.Resolve(new KuroRelease { CdnList = [mirror] }, relative);
            try { await DownloadAsync(url, destination, file, token, progress); return; }
            catch (Exception ex) when (ex is HttpRequestException or IOException || ex is OperationCanceledException && !token.IsCancellationRequested)
            { last = ex; await Task.Delay(300, token); }
        }
        throw new IOException("All Kuro download mirrors failed.", last);
    }

    public async Task DownloadAsync(Uri url, string destination, KuroFile file, CancellationToken token, Action<long>? progress = null)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        if (await MatchesAsync(destination, file, token)) return;
        string partial = destination + ".part";
        long offset = File.Exists(partial) ? new FileInfo(partial).Length : 0;
        if (offset >= file.Size) { File.Delete(partial); offset = 0; }
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (offset > 0) request.Headers.Range = new RangeHeaderValue(offset, null);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        if (response.StatusCode == HttpStatusCode.PartialContent)
        {
            var range = response.Content.Headers.ContentRange;
            if (range?.From != offset || range.To != file.Size - 1 || range.Length != file.Size)
                throw new InvalidDataException("Invalid download range.");
        }
        else if (response.StatusCode == HttpStatusCode.OK) offset = 0;
        else throw new InvalidDataException("Unexpected download response.");
        await using (var output = new FileStream(partial, offset == 0 ? FileMode.Create : FileMode.Append, FileAccess.Write, FileShare.None))
        await using (var input = await response.Content.ReadAsStreamAsync(token))
        {
            byte[] buffer = new byte[128 * 1024];
            int count;
            while ((count = await input.ReadAsync(buffer, token)) != 0)
            {
                if (offset + count > file.Size) throw new InvalidDataException("Download exceeds manifest size.");
                if (throttle is not null) await throttle(count, token);
                await output.WriteAsync(buffer.AsMemory(0, count), token);
                downloaded?.Invoke(count);
                offset += count; progress?.Invoke(offset);
            }
        }
        if (!await MatchesAsync(partial, file, token))
        { File.Delete(partial); throw new InvalidDataException("Downloaded file checksum mismatch."); }
        File.Move(partial, destination, true);
    }

    public static async Task<bool> MatchesAsync(string path, KuroFile file, CancellationToken token = default)
    {
        if (!File.Exists(path) || new FileInfo(path).Length != file.Size) return false;
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await MD5.HashDataAsync(stream, token)).Equals(NormalizeMd5(file.Md5), StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeMd5(string value)
    {
        if (value.Length != 32 || !value.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid manifest MD5.");
        return value.PadLeft(32, '0').ToUpperInvariant();
    }

    private static void Validate(string root, KuroFile file)
    { ValidateDest(root, file.Dest); _ = NormalizeMd5(file.Md5); if (file.Size < 0) throw new InvalidDataException("Invalid file size."); }

    private static void ValidateDest(string root, string path)
    {
        _ = GameFilePath.Resolve(root, path);
        if (path.StartsWith(".tideward", StringComparison.OrdinalIgnoreCase) || path.StartsWith("Client/Saved/", StringComparison.OrdinalIgnoreCase)
            || path.Equals("launcherDownloadConfig.json", StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Manifest targets local user data.");
    }

    public sealed record Change(string Path, bool HadOriginal, bool Delete);
    public sealed record Journal(string Version, List<Change> Changes);
}
