using System.Text.Json;

namespace MyGit;

/// <summary>
/// 一个存档点（游戏存档槽）的元数据，持久化在存档目录的 meta.json。
/// 文件内容本身存放在同目录 files\ 下，本类型只负责元数据读写。
/// </summary>
public sealed class SavePoint
{
    /// <summary>存档唯一 id（同时是存档目录名，按时间排序友好）。</summary>
    public required string Id { get; init; }

    /// <summary>存档显示名（新建存档默认「项目名 + 时间」，可重命名）。</summary>
    public required string Name { get; set; }

    /// <summary>创建时间（UTC，展示时转本地）。</summary>
    public required DateTime CreatedUtc { get; init; }

    /// <summary>快照包含的文件数。</summary>
    public required long FileCount { get; init; }

    /// <summary>快照文件总字节数。</summary>
    public required long TotalBytes { get; init; }

    /// <summary>是否为读档前自动创建的恢复存档。</summary>
    public required bool IsAuto { get; init; }

    /// <summary>创建时间（本地时区，用于界面展示）。</summary>
    public DateTime CreatedLocal => CreatedUtc.ToLocalTime();

    private sealed class Dto
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public DateTime CreatedUtc { get; set; }
        public long FileCount { get; set; }
        public long TotalBytes { get; set; }
        public bool IsAuto { get; set; }
    }

    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    /// <summary>把元数据写入 meta.json（覆盖）。</summary>
    public static void Write(string metaPath, SavePoint sp)
    {
        var dto = new Dto
        {
            Id = sp.Id,
            Name = sp.Name,
            CreatedUtc = sp.CreatedUtc,
            FileCount = sp.FileCount,
            TotalBytes = sp.TotalBytes,
            IsAuto = sp.IsAuto,
        };
        File.WriteAllText(metaPath, JsonSerializer.Serialize(dto, JsonOpts));
    }

    /// <summary>读取 meta.json；文件缺失或字段不完整时抛出 <see cref="IOException"/>。</summary>
    public static SavePoint Read(string metaPath)
    {
        var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(metaPath))
            ?? throw new IOException("存档元数据损坏: " + metaPath);
        if (string.IsNullOrWhiteSpace(dto.Id) || string.IsNullOrWhiteSpace(dto.Name))
            throw new IOException("存档元数据缺字段: " + metaPath);
        return new SavePoint
        {
            Id = dto.Id,
            Name = dto.Name,
            CreatedUtc = dto.CreatedUtc,
            FileCount = dto.FileCount,
            TotalBytes = dto.TotalBytes,
            IsAuto = dto.IsAuto,
        };
    }
}
