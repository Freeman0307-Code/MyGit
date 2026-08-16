using System.Security.Cryptography;
using System.Text.Json;

namespace MyGit;

/// <summary>
/// 存档内容清单（manifest.json）：项目目录在存档时刻的完整文件列表
/// （相对路径 / 大小 / 修改时间 / SHA256）与全部子目录。
/// 相对路径统一用 '/' 分隔、不以 '/' 开头、不含 '..'。
/// </summary>
public sealed class Manifest
{
    public List<ManifestEntry> Files { get; set; } = new();
    public List<string> Directories { get; set; } = new();

    /// <summary>清单中所有文件的字节总数。</summary>
    public long TotalBytes => Files.Sum(f => f.Size);

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    /// <summary>
    /// 扫描 root 目录生成清单：跳过 excludedDirNames 中列出的目录（大小写不敏感，按目录名匹配）
    /// 与符号链接/联接点（防循环），每个文件计算 SHA256（用于读档前判断「未保存改动」）。
    /// 扫描失败（如权限不足）时异常上抛，由调用方提示。
    /// </summary>
    public static Manifest Build(string root, ISet<string> excludedDirNames)
    {
        var manifest = new Manifest();
        var stack = new Stack<string>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            string dir = stack.Pop();
            foreach (string sub in Directory.EnumerateDirectories(dir))
            {
                var info = new DirectoryInfo(sub);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue; // 符号链接/联接点：跳过，避免循环与越界
                if (excludedDirNames.Contains(info.Name)
                    || info.Name.StartsWith(".mygit", StringComparison.OrdinalIgnoreCase)) continue; // 排除目录与存档库（.mygit/.mygit-xxx）不进快照
                manifest.Directories.Add(NormalizeRel(Path.GetRelativePath(root, sub)));
                stack.Push(sub);
            }
            foreach (string file in Directory.EnumerateFiles(dir))
            {
                var info = new FileInfo(file);
                if ((info.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                string rel = NormalizeRel(Path.GetRelativePath(root, file));
                manifest.Files.Add(new ManifestEntry
                {
                    Path = rel,
                    Size = info.Length,
                    UtcTicks = info.LastWriteTimeUtc.Ticks,
                    Sha256 = HashFile(file),
                });
            }
        }
        manifest.Directories.Sort(StringComparer.Ordinal);
        manifest.Files.Sort((a, b) => StringComparer.Ordinal.Compare(a.Path, b.Path));
        return manifest;
    }

    /// <summary>读取 manifest.json 并校验每条路径（持久化边界：拒绝绝对路径与 '..' 越界）。</summary>
    public static Manifest Read(string path)
    {
        var manifest = JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path))
            ?? throw new IOException("清单文件损坏: " + path);
        manifest.Files ??= new List<ManifestEntry>();
        manifest.Directories ??= new List<string>();
        foreach (var f in manifest.Files)
        {
            if (string.IsNullOrWhiteSpace(f.Path)) throw new IOException("清单含空路径: " + path);
            ValidateRelPath(f.Path);
        }
        foreach (var d in manifest.Directories)
            ValidateRelPath(d);
        return manifest;
    }

    /// <summary>写 manifest.json。</summary>
    public void Write(string path) =>
        File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOpts));

    /// <summary>
    /// 对比当前目录状态（current）与存档清单（baseline，可为 null 表示「空存档」）。
    /// 内容以 SHA256 为准：任何大小/时间/哈希差异都算「已修改」。
    /// </summary>
    public static DiffResult Diff(Manifest current, Manifest? baseline)
    {
        var result = new DiffResult();
        if (baseline == null)
        {
            result.Added.AddRange(current.Files.Select(f => f.Path));
            result.Added.Sort(StringComparer.Ordinal);
            return result;
        }

        var baseMap = new Dictionary<string, ManifestEntry>(StringComparer.Ordinal);
        foreach (var f in baseline.Files)
            baseMap[f.Path] = f;

        foreach (var f in current.Files)
        {
            if (!baseMap.TryGetValue(f.Path, out var b))
                result.Added.Add(f.Path);
            else if (b.Size != f.Size || b.UtcTicks != f.UtcTicks
                     || !string.Equals(b.Sha256, f.Sha256, StringComparison.Ordinal))
                result.Modified.Add(f.Path);
            baseMap.Remove(f.Path);
        }
        result.Deleted.AddRange(baseMap.Keys);

        result.Added.Sort(StringComparer.Ordinal);
        result.Modified.Sort(StringComparer.Ordinal);
        result.Deleted.Sort(StringComparer.Ordinal);
        return result;
    }

    /// <summary>计算文件 SHA256（小写十六进制）。以 FileShare.ReadWrite|Delete 打开，避免被占用文件卡住。</summary>
    public static string HashFile(string path)
    {
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
        byte[] buf = new byte[1 << 16];
        int n;
        while ((n = fs.Read(buf, 0, buf.Length)) > 0)
            sha.AppendData(buf, 0, n);
        return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
    }

    /// <summary>校验相对路径：不能是根路径、不能含 '..' 段。</summary>
    private static void ValidateRelPath(string rel)
    {
        string normalized = rel.Replace('\\', '/');
        if (Path.IsPathRooted(rel) || normalized.StartsWith('/'))
            throw new IOException("清单含绝对路径: " + rel);
        if (normalized.Split('/').Any(seg => seg == ".."))
            throw new IOException("清单含越界路径: " + rel);
    }

    private static string NormalizeRel(string rel) =>
        rel.Replace(Path.DirectorySeparatorChar, '/');
}

/// <summary>清单里的单个文件条目。</summary>
public sealed class ManifestEntry
{
    public string Path { get; set; } = "";
    public long Size { get; set; }
    public long UtcTicks { get; set; }
    public string Sha256 { get; set; } = "";
}

/// <summary>当前工作区相对某存档的差异：新增 / 修改 / 删除 的文件路径列表。</summary>
public sealed class DiffResult
{
    public List<string> Added { get; } = new();
    public List<string> Modified { get; } = new();
    public List<string> Deleted { get; } = new();

    public bool Dirty => Added.Count > 0 || Modified.Count > 0 || Deleted.Count > 0;
    public int ChangeCount => Added.Count + Modified.Count + Deleted.Count;
}
