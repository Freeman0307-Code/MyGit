using System.Text.Json;

namespace MyGit;

/// <summary>
/// MyGit 项目快照引擎：纯文件操作（不依赖 git）。把一个项目目录当作「游戏进度」管理：
/// 存档 = 把项目目录完整复制进 .mygit\saves\<id>\files\；读档 = 用存档内容整体还原项目目录。
/// 读档前若工作区有未保存改动，先自动创建「自动恢复存档」，防止误操作丢进度。
///
/// 磁盘布局（<项目目录> 下）：
///   .mygit\project.json            项目信息（名称 / id / 当前存档点）
///   .mygit\saves\<id>\meta.json    存档元数据
///   .mygit\saves\<id>\manifest.json 存档文件清单（含 SHA256）
///   .mygit\saves\<id>\files\...     存档文件内容（目录镜像）
/// </summary>
public sealed class ProjectStore
{
    public const string StoreDirName = ".mygit";

    /// <summary>存档库目录名前缀（.mygit 与 .mygit-xxx 都按此前缀识别/跳过）。</summary>
    public const string StoreDirPrefix = ".mygit";

    /// <summary>Unity 项目导入时自动排除的重建目录（都可由 Unity 自动重新生成）。</summary>
    public static readonly string[] UnityDefaultExcludes = { "Library", "Temp", "Logs", "obj", "Build", "Builds" };

    private sealed class ProjectDto
    {
        public int Format { get; set; } = 1;
        public string Name { get; set; } = "";
        public string Id { get; set; } = "";
        public DateTime CreatedUtc { get; set; }
        public string? CurrentSaveId { get; set; }
        public List<string> ExcludeDirs { get; set; } = new();

        /// <summary>被管理目录的绝对路径；null = 存档库在项目目录内（.mygit）。</summary>
        public string? Root { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private readonly object _gate = new();

    /// <summary>项目目录绝对路径（被管理的目录）。</summary>
    public string ProjectDir { get; }

    /// <summary>项目名（与目录名一致；外部存档库项目为「Unity项目名/子目录名」）。</summary>
    public string ProjectName { get; }

    /// <summary>存档库目录（默认在项目目录内的 .mygit；外部存档库项目放在别处，如 Unity 项目根目录）。</summary>
    public string StoreDir { get; }

    /// <summary>存档库是否在项目目录之外（外部存档库：导入 Unity Assets 等场景）。</summary>
    public bool IsExternal => !string.Equals(StoreDir, Path.Combine(ProjectDir, StoreDirName), StringComparison.OrdinalIgnoreCase);

    /// <summary>所有存档的父目录。</summary>
    public string SavesDir => Path.Combine(StoreDir, "saves");

    private string ProjectJsonPath => Path.Combine(StoreDir, "project.json");
    private string SaveDirOf(string id) => Path.Combine(SavesDir, id);

    private ProjectStore(string projectDir, string name, string? storeDir = null)
    {
        ProjectDir = Path.GetFullPath(projectDir);
        ProjectName = name;
        StoreDir = Path.GetFullPath(storeDir ?? Path.Combine(ProjectDir, StoreDirName));
    }

    // ======================== 项目生命周期 ========================

    /// <summary>目录是否为 MyGit 项目（存档库在目录内的 .mygit）。</summary>
    public static bool IsProjectDir(string dir) =>
        Directory.Exists(dir) && File.Exists(Path.Combine(dir, StoreDirName, "project.json"));

    /// <summary>目录是否已被 MyGit 管理（内部存档库或外部存档库任一存在）。</summary>
    public static bool IsManagedDir(string dir) =>
        IsProjectDir(dir) || FindExternalStore(dir) != null;

    /// <summary>打开已有项目目录：优先内部存档库（.mygit），其次外部存档库（如 Unity 根目录下的 .mygit-Assets）。</summary>
    public static ProjectStore Open(string projectDir)
    {
        if (IsProjectDir(projectDir))
        {
            var dto = ReadProject(Path.Combine(projectDir, StoreDirName, "project.json"));
            return new ProjectStore(projectDir, dto.Name);
        }
        string? ext = FindExternalStore(projectDir);
        if (ext != null)
        {
            var dto = ReadProject(Path.Combine(ext, "project.json"));
            return new ProjectStore(projectDir, dto.Name, ext);
        }
        throw new IOException($"不是 MyGit 项目目录（缺少 {StoreDirName}\\project.json，也不是已导入的外部目录）: {projectDir}");
    }

    /// <summary>校验项目名：非空、无非法文件名字符、长度与结尾规则。</summary>
    public static void ValidateProjectName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("项目名不能为空");
        if (name is "." or "..") throw new ArgumentException("项目名不能是 . 或 ..");
        if (name.Length > 60) throw new ArgumentException("项目名过长（最多 60 个字符）");
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new ArgumentException("项目名含非法字符: " + string.Join(" ", name.Where(Path.GetInvalidFileNameChars().Contains)));
        if (name.EndsWith(' ') || name.EndsWith('.')) throw new ArgumentException("项目名不能以空格或 . 结尾");
    }

    /// <summary>在 baseDir 下新建项目（目录 + 存档库），返回已打开的项目句柄。</summary>
    public static ProjectStore Create(string baseDir, string name)
    {
        ValidateProjectName(name);
        string dir = Path.Combine(baseDir, name);
        if (Directory.Exists(dir)) throw new IOException("项目目录已存在: " + dir);
        Directory.CreateDirectory(Path.Combine(dir, StoreDirName, "saves"));
        var store = new ProjectStore(dir, name);
        store.WriteProject(new ProjectDto
        {
            Name = name,
            Id = Guid.NewGuid().ToString("N"),
            CreatedUtc = DateTime.UtcNow,
            CurrentSaveId = null,
        });
        return store;
    }

    /// <summary>
    /// 导入（采纳）一个已有目录：在目录内创建存档库 .mygit，原地纳入 MyGit 管理。
    /// 不移动、不复制、不修改目录里的任何现有文件；项目名取目录名，当前存档点为空
    /// （现有文件会显示为「未保存的新增」，存档一次即成为第一个存档点）。
    /// Unity 项目（含 Assets/ 与 ProjectSettings/）自动排除 Library/Temp/Logs/obj/Build/Builds。
    /// </summary>
    public static ProjectStore Adopt(string dir)
    {
        string full = Path.GetFullPath(dir);
        if (!Directory.Exists(full)) throw new IOException("目录不存在: " + full);
        string name = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(name)) throw new IOException("不能导入磁盘根目录: " + full);
        if (IsManagedDir(full)) throw new IOException("该目录已经被 MyGit 管理，直接 open 即可: " + full);
        Directory.CreateDirectory(Path.Combine(full, StoreDirName, "saves"));
        var store = new ProjectStore(full, name);
        var dto = new ProjectDto
        {
            Name = name,
            Id = Guid.NewGuid().ToString("N"),
            CreatedUtc = DateTime.UtcNow,
            CurrentSaveId = null,
        };
        if (IsUnityStyleProject(full))
            dto.ExcludeDirs = UnityDefaultExcludes.ToList();
        store.WriteProject(dto);
        return store;
    }

    /// <summary>
    /// 外部存档库导入：管理 Unity 的 Assets（或其子目录），存档库放在 Unity 项目根目录
    /// （如 .mygit-Assets），Unity 不会扫描到存档副本。项目名显示为「Unity项目名/子目录名」。
    /// </summary>
    public static ProjectStore AdoptExternal(string dir)
    {
        string full = Path.GetFullPath(dir);
        if (!Directory.Exists(full)) throw new IOException("目录不存在: " + full);
        string leaf = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(leaf)) throw new IOException("不能导入磁盘根目录: " + full);
        string unityRoot = FindUnityRootOf(full) ?? throw new IOException("目录不在 Unity 的 Assets 内，请用普通导入: " + full);

        string store = Path.Combine(unityRoot, StoreDirPrefix + "-" + leaf);
        string pj = Path.Combine(store, "project.json");
        if (File.Exists(pj))
        {
            var existing = ReadProject(pj);
            if (existing.Root != null && !Path.GetFullPath(existing.Root).Equals(full, StringComparison.OrdinalIgnoreCase))
                throw new IOException($"同名存档库已存在且指向其他目录: {store}");
            throw new IOException("该目录已导入过（外部存档库已存在），直接 open 即可: " + full);
        }

        Directory.CreateDirectory(Path.Combine(store, "saves"));
        string unityName = Path.GetFileName(unityRoot.TrimEnd(Path.DirectorySeparatorChar));
        var s = new ProjectStore(full, unityName + "/" + leaf, store);
        s.WriteProject(new ProjectDto
        {
            Name = s.ProjectName,
            Id = Guid.NewGuid().ToString("N"),
            CreatedUtc = DateTime.UtcNow,
            CurrentSaveId = null,
            Root = full,
        });
        return s;
    }

    /// <summary>目录是否像 Unity 项目（同时存在 Assets 与 ProjectSettings）。</summary>
    public static bool IsUnityStyleProject(string dir) =>
        Directory.Exists(Path.Combine(dir, "Assets")) && Directory.Exists(Path.Combine(dir, "ProjectSettings"));

    /// <summary>若目录位于 Unity 的 Assets 内（自身或任意祖先名为 Assets），返回 Unity 项目根目录（Assets 的父目录）；否则 null。</summary>
    public static string? FindUnityRootOf(string dir)
    {
        string? cur = Path.GetFullPath(dir).TrimEnd(Path.DirectorySeparatorChar);
        while (cur != null && Path.GetFileName(cur).Length > 0)
        {
            string? parent = Path.GetDirectoryName(cur);
            if (parent == null) return null;
            if (Path.GetFileName(cur).Equals("Assets", StringComparison.OrdinalIgnoreCase))
                return parent;
            cur = parent.TrimEnd(Path.DirectorySeparatorChar);
        }
        return null;
    }

    /// <summary>查找目录对应的外部存档库（Unity 根目录下的 .mygit-<目录名> 且 Root 指向该目录）；没有则 null。</summary>
    public static string? FindExternalStore(string dir)
    {
        string full = Path.GetFullPath(dir);
        string leaf = Path.GetFileName(full.TrimEnd(Path.DirectorySeparatorChar));
        if (string.IsNullOrWhiteSpace(leaf)) return null;
        string? unityRoot = FindUnityRootOf(full);
        if (unityRoot == null) return null;
        string pj = Path.Combine(unityRoot, StoreDirPrefix + "-" + leaf, "project.json");
        if (!File.Exists(pj)) return null;
        try
        {
            var dto = ReadProject(pj);
            return dto.Root != null && Path.GetFullPath(dto.Root).Equals(full, StringComparison.OrdinalIgnoreCase)
                ? Path.GetDirectoryName(pj)
                : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            // 存档库元数据损坏：视为未找到
            return null;
        }
    }

    /// <summary>列出 baseDir 下所有项目（目录 + 项目名，按名字排序）；损坏的项目跳过。</summary>
    public static IReadOnlyList<(string Dir, string Name)> ListProjects(string baseDir)
    {
        var list = new List<(string Dir, string Name)>();
        if (!Directory.Exists(baseDir)) return list;
        foreach (string dir in Directory.EnumerateDirectories(baseDir))
        {
            string pj = Path.Combine(dir, StoreDirName, "project.json");
            if (!File.Exists(pj)) continue;
            try { list.Add((Dir: dir, Name: ReadProject(pj).Name)); }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
                // 项目元数据损坏：跳过，不拖垮整个列表
            }
        }
        list.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
        return list;
    }

    /// <summary>
    /// 永久删除项目。内部存档库项目：删除整个目录（含全部存档）；
    /// 外部存档库项目：只删除存档库，被管理的目录原样保留。
    /// </summary>
    public static void DeleteProjectAt(string projectDir)
    {
        string full = Path.GetFullPath(projectDir);
        string? ext = FindExternalStore(full);
        if (ext != null)
        {
            DeleteEntry(ext);
            return;
        }
        if (!IsProjectDir(full))
            throw new IOException("不是 MyGit 项目目录，拒绝删除: " + full);
        DeleteEntry(full);
    }

    // ======================== 存档查询 ========================

    /// <summary>当前存档点（project.json 记录的 id；文件缺失时自动清空记录并返回 null）。</summary>
    public SavePoint? CurrentSave
    {
        get
        {
            lock (_gate)
            {
                var dto = ReadProject(ProjectJsonPath);
                if (dto.CurrentSaveId == null) return null;
                string meta = Path.Combine(SaveDirOf(dto.CurrentSaveId), "meta.json");
                if (!File.Exists(meta))
                {
                    dto.CurrentSaveId = null;
                    WriteProject(dto);
                    return null;
                }
                return SavePoint.Read(meta);
            }
        }
    }

    /// <summary>存档/改动检测时跳过的目录名列表（.mygit 始终排除，不在列表里）。</summary>
    public IReadOnlyList<string> ExcludeDirs
    {
        get
        {
            lock (_gate)
            {
                return ReadProject(ProjectJsonPath).ExcludeDirs.ToList();
            }
        }
    }

    /// <summary>
    /// 整体替换排除目录列表：每个名字必须是合法目录名（不能含路径分隔符、不能是 . 或 ..）。
    /// .mygit 名会被忽略（它始终被排除）；重复名自动去重排序。
    /// </summary>
    public void SetExcludeDirs(IEnumerable<string> names)
    {
        var cleaned = new List<string>();
        foreach (string raw in names)
        {
            string n = raw.Trim();
            if (n.Length == 0) continue;
            if (n.Equals(StoreDirName, StringComparison.OrdinalIgnoreCase)) continue;
            if (n is "." or ".." || n.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                throw new ArgumentException("非法目录名: " + raw);
            cleaned.Add(n);
        }
        lock (_gate)
        {
            var dto = ReadProject(ProjectJsonPath);
            dto.ExcludeDirs = cleaned.Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
            WriteProject(dto);
        }
    }

    /// <summary>全部存档，按创建时间最新在前。</summary>
    public IReadOnlyList<SavePoint> ListSaves()
    {
        lock (_gate)
        {
            var list = new List<SavePoint>();
            if (Directory.Exists(SavesDir))
            {
                foreach (string dir in Directory.EnumerateDirectories(SavesDir))
                {
                    string meta = Path.Combine(dir, "meta.json");
                    if (File.Exists(meta))
                    {
                        try { list.Add(SavePoint.Read(meta)); }
                        catch (Exception ex) when (ex is IOException or JsonException)
                        {
                            // 单个存档元数据损坏：跳过，其余存档仍可用
                        }
                    }
                }
            }
            list.Sort((a, b) =>
            {
                int t = b.CreatedUtc.CompareTo(a.CreatedUtc);
                return t != 0 ? t : StringComparer.Ordinal.Compare(b.Id, a.Id);
            });
            return list;
        }
    }

    // ======================== 核心操作 ========================

    /// <summary>创建存档：完整快照当前项目目录，并设为当前存档点。progress 用于回传进度消息（可能来自后台线程）。</summary>
    public SavePoint Save(string name, bool isAuto, Action<string>? progress)
    {
        lock (_gate)
        {
            string id = DateTime.Now.ToString("yyyyMMdd-HHmmss-") + Guid.NewGuid().ToString("N")[..4];
            string filesDir = Path.Combine(SaveDirOf(id), "files");
            Directory.CreateDirectory(filesDir);

            progress?.Invoke("扫描项目文件…");
            var manifest = Manifest.Build(ProjectDir, BuildExcludeSet());
            progress?.Invoke($"扫描完成：{manifest.Files.Count} 个文件 · {manifest.TotalBytes} 字节");

            int copied = 0;
            foreach (var entry in manifest.Files)
            {
                CopyFile(ResolveUnder(ProjectDir, entry.Path), Path.Combine(filesDir, entry.Path.Replace('/', Path.DirectorySeparatorChar)));
                copied++;
                if (copied % 50 == 0) progress?.Invoke($"已复制 {copied}/{manifest.Files.Count} 个文件…");
            }
            manifest.Write(Path.Combine(SaveDirOf(id), "manifest.json"));

            var sp = new SavePoint
            {
                Id = id,
                Name = name,
                CreatedUtc = DateTime.UtcNow,
                FileCount = manifest.Files.Count,
                TotalBytes = manifest.TotalBytes,
                IsAuto = isAuto,
            };
            SavePoint.Write(Path.Combine(SaveDirOf(id), "meta.json"), sp);

            var dto = ReadProject(ProjectJsonPath);
            dto.CurrentSaveId = id;
            WriteProject(dto);
            return sp;
        }
    }

    /// <summary>
    /// 读档（硬重置）：用存档内容整体还原项目目录。autoRecover 为 true 时，
    /// 若当前工作区相对当前存档有未保存改动，先自动创建「自动恢复存档」再还原。
    /// </summary>
    public LoadResult Load(SavePoint target, bool autoRecover, Action<string>? progress)
    {
        lock (_gate)
        {
            var status = GetStatusCore();
            SavePoint? recovered = null;
            if (autoRecover && status.Dirty)
            {
                recovered = Save($"自动恢复存档 {DateTime.Now:yyyy-MM-dd HH:mm:ss}", isAuto: true, progress);
                progress?.Invoke($"已创建自动恢复存档: {recovered.Name}");
            }

            var manifest = Manifest.Read(Path.Combine(SaveDirOf(target.Id), "manifest.json")); // 先读清单并校验路径，失败则不动工作区

            // 1) 清空项目目录：保留存档库本身（含外部）、保留排除目录（读档不影响它们，如 Unity 的 Library）
            progress?.Invoke("清空当前项目目录（保留排除目录）…");
            var excludes = BuildExcludeSet();
            foreach (string entry in Directory.EnumerateFileSystemEntries(ProjectDir))
            {
                string entryName = Path.GetFileName(entry);
                if (excludes.Contains(entryName) || entryName.StartsWith(StoreDirPrefix, StringComparison.OrdinalIgnoreCase)) continue;
                DeleteEntry(entry);
            }

            // 2) 重建目录结构
            foreach (string rel in manifest.Directories)
            {
                string dir = ResolveUnder(ProjectDir, rel);
                Directory.CreateDirectory(dir);
            }

            // 3) 还原文件内容与修改时间
            string filesDir = Path.Combine(SaveDirOf(target.Id), "files");
            int done = 0;
            foreach (var entry in manifest.Files)
            {
                string dst = ResolveUnder(ProjectDir, entry.Path);
                CopyFile(Path.Combine(filesDir, entry.Path.Replace('/', Path.DirectorySeparatorChar)), dst);
                File.SetLastWriteTimeUtc(dst, new DateTime(entry.UtcTicks, DateTimeKind.Utc));
                done++;
                if (done % 50 == 0) progress?.Invoke($"已还原 {done}/{manifest.Files.Count} 个文件…");
            }

            var dto = ReadProject(ProjectJsonPath);
            dto.CurrentSaveId = target.Id;
            WriteProject(dto);
            return new LoadResult { Recovered = recovered };
        }
    }

    /// <summary>删除存档（含全部快照数据）。删除的是当前存档点时，当前存档自动落到最新剩余存档。</summary>
    public void DeleteSave(SavePoint sp, Action<string>? progress)
    {
        lock (_gate)
        {
            string dir = SaveDirOf(sp.Id);
            if (Directory.Exists(dir)) DeleteEntry(dir);
            progress?.Invoke($"已删除快照数据: {sp.Name}");
            var dto = ReadProject(ProjectJsonPath);
            if (string.Equals(dto.CurrentSaveId, sp.Id, StringComparison.Ordinal))
            {
                dto.CurrentSaveId = ListSaves().FirstOrDefault()?.Id;
                WriteProject(dto);
            }
        }
    }

    /// <summary>重命名存档（只改元数据，不动快照内容）。</summary>
    public void RenameSave(SavePoint sp, string newName)
    {
        if (string.IsNullOrWhiteSpace(newName)) throw new ArgumentException("存档名不能为空");
        lock (_gate)
        {
            SavePoint.Write(Path.Combine(SaveDirOf(sp.Id), "meta.json"), new SavePoint
            {
                Id = sp.Id,
                Name = newName.Trim(),
                CreatedUtc = sp.CreatedUtc,
                FileCount = sp.FileCount,
                TotalBytes = sp.TotalBytes,
                IsAuto = sp.IsAuto,
            });
        }
    }

    /// <summary>对比当前工作区与当前存档的差异（无当前存档时全部文件视为新增）。</summary>
    public DiffResult GetStatus()
    {
        lock (_gate) { return GetStatusCore(); }
    }

    private DiffResult GetStatusCore()
    {
        var current = Manifest.Build(ProjectDir, BuildExcludeSet());
        Manifest? baseline = null;
        var dto = ReadProject(ProjectJsonPath);
        if (dto.CurrentSaveId != null)
        {
            string manifestPath = Path.Combine(SaveDirOf(dto.CurrentSaveId), "manifest.json");
            if (File.Exists(manifestPath)) baseline = Manifest.Read(manifestPath);
        }
        return Manifest.Diff(current, baseline);
    }

    // ======================== 内部工具 ========================

    /// <summary>构建扫描排除集：项目配置的排除目录 + 存档库本身（大小写不敏感）。</summary>
    private ISet<string> BuildExcludeSet()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { StoreDirName };
        foreach (var n in ReadProject(ProjectJsonPath).ExcludeDirs)
            set.Add(n);
        return set;
    }

    /// <summary>把相对清单路径解析到 root 下并校验不越界（持久化边界）。</summary>
    private static string ResolveUnder(string root, string rel)
    {
        string full = Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
        string rootPrefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            throw new IOException("路径越出项目目录: " + rel);
        return full;
    }

    /// <summary>复制文件并保持修改时间（父目录不存在时自动创建）。</summary>
    private static void CopyFile(string src, string dst)
    {
        string? dir = Path.GetDirectoryName(dst);
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.Copy(src, dst, overwrite: true);
        File.SetLastWriteTimeUtc(dst, File.GetLastWriteTimeUtc(src));
    }

    /// <summary>递归删除文件/目录（先清只读属性；符号链接/联接点直接删除不递归）。</summary>
    private static void DeleteEntry(string path)
    {
        var attrs = File.GetAttributes(path);
        if ((attrs & FileAttributes.ReparsePoint) != 0)
        {
            if ((attrs & FileAttributes.Directory) != 0) Directory.Delete(path, recursive: false);
            else File.Delete(path);
            return;
        }
        if ((attrs & FileAttributes.Directory) != 0)
        {
            foreach (string child in Directory.EnumerateFileSystemEntries(path))
                DeleteEntry(child);
            File.SetAttributes(path, FileAttributes.Normal);
            Directory.Delete(path, recursive: false);
        }
        else
        {
            File.SetAttributes(path, FileAttributes.Normal);
            File.Delete(path);
        }
    }

    private static ProjectDto ReadProject(string path)
    {
        var dto = JsonSerializer.Deserialize<ProjectDto>(File.ReadAllText(path))
            ?? throw new IOException("项目元数据损坏: " + path);
        if (string.IsNullOrWhiteSpace(dto.Name)) throw new IOException("项目元数据缺名称: " + path);
        return dto;
    }

    private void WriteProject(ProjectDto dto) =>
        File.WriteAllText(ProjectJsonPath, JsonSerializer.Serialize(dto, JsonOpts));
}

/// <summary>读档结果：是否在读档前自动创建了恢复存档。</summary>
public sealed class LoadResult
{
    /// <summary>自动恢复存档（工作区原本干净时为 null）。</summary>
    public SavePoint? Recovered { get; init; }
}
