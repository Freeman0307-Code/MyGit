using System.Text.Json;

namespace MyGit;

/// <summary>外部存档库项目的登记项（管理目录 + 存档库位置）。</summary>
public sealed class ExternalProject
{
    public string Dir { get; set; } = "";
    public string StoreDir { get; set; } = "";
}

/// <summary>
/// MyGit 应用配置：项目库目录、外部项目登记、上次打开的项目，
/// 持久化在应用根目录 mygit.config.json。
/// 应用根目录 = 向上找到 MyGit.csproj 的目录（开发期 dotnet run 也能正确定位），
/// 找不到（如发布后的独立 exe）时退回程序集目录。
/// </summary>
public static class AppConfig
{
    private sealed class Data
    {
        public string ProjectsBase { get; set; } = "Projects";
        public string? LastProject { get; set; }
        public List<ExternalProject> External { get; set; } = new();
    }

    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    /// <summary>应用根目录（配置与默认项目库存放处）。</summary>
    public static string AppRoot { get; } = FindAppRoot();

    /// <summary>配置文件路径。</summary>
    public static string ConfigPath => Path.Combine(AppRoot, "mygit.config.json");

    /// <summary>项目库目录（new 命令创建的项目放这里）。相对路径按应用根目录解析。</summary>
    public static string ProjectsBaseDir
    {
        get
        {
            string rel = Load().ProjectsBase;
            return Path.IsPathRooted(rel) ? rel : Path.GetFullPath(Path.Combine(AppRoot, rel));
        }
    }

    /// <summary>上次打开的项目目录（可能不存在）。</summary>
    public static string? LastProject => Load().LastProject;

    /// <summary>修改项目库目录（立即持久化）。</summary>
    public static void SetProjectsBase(string dir)
    {
        var data = Load();
        data.ProjectsBase = Path.GetFullPath(dir);
        Save(data);
    }

    /// <summary>记录上次打开的项目。</summary>
    public static void SetLastProject(string? path)
    {
        var data = Load();
        data.LastProject = path;
        Save(data);
    }

    /// <summary>已登记的外部存档库项目（管理目录不在项目库里的项目）。</summary>
    public static IReadOnlyList<ExternalProject> ExternalProjects => Load().External;

    /// <summary>登记外部存档库项目（按管理目录去重，重复登记时刷新存档库位置）。</summary>
    public static void RegisterExternal(string dir, string storeDir)
    {
        var data = Load();
        string full = Path.GetFullPath(dir);
        var hit = data.External.FirstOrDefault(e =>
            string.Equals(Path.GetFullPath(e.Dir), full, StringComparison.OrdinalIgnoreCase));
        if (hit != null)
        {
            hit.Dir = full;
            hit.StoreDir = Path.GetFullPath(storeDir);
        }
        else
        {
            data.External.Add(new ExternalProject { Dir = full, StoreDir = Path.GetFullPath(storeDir) });
        }
        Save(data);
    }

    /// <summary>注销外部存档库项目（按管理目录匹配）。</summary>
    public static void UnregisterExternal(string dir)
    {
        var data = Load();
        string full = Path.GetFullPath(dir);
        data.External.RemoveAll(e =>
            string.Equals(Path.GetFullPath(e.Dir), full, StringComparison.OrdinalIgnoreCase));
        Save(data);
    }

    private static Data Load()
    {
        lock (Gate)
        {
            try
            {
                if (File.Exists(ConfigPath))
                    return JsonSerializer.Deserialize<Data>(File.ReadAllText(ConfigPath)) ?? new Data();
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                // 配置损坏或不可读：回退默认值（下次保存会覆盖）
            }
            return new Data();
        }
    }

    private static void Save(Data data)
    {
        lock (Gate)
        {
            try
            {
                File.WriteAllText(ConfigPath, JsonSerializer.Serialize(data, JsonOpts));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // 配置不可写（如程序目录只读）：本次修改仅本会话生效，不打断使用
            }
        }
    }

    private static string FindAppRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "MyGit.csproj")))
                return dir.FullName;
        return AppContext.BaseDirectory;
    }
}
