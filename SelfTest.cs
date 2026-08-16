using System.Text;

namespace MyGit;

/// <summary>
/// 快照引擎自检（--selftest）：在临时目录跑一遍
/// 建项目 → 存档 → 改动 → 读档(自动恢复) → 重命名 → 删除 → 清单越界防护 → 删项目 全流程。
/// 结果写 stdout 与程序集目录下的 selftest.log。
/// </summary>
public static class SelfTest
{
    public static bool Run()
    {
        var log = new StringBuilder();
        int fail = 0;
        void Check(string what, bool ok)
        {
            log.AppendLine(ok ? $"[PASS] {what}" : $"[FAIL] {what}");
            if (!ok) fail++;
        }

        string tempRoot = Path.Combine(Path.GetTempPath(), "MyGitSelfTest-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            // ---- 建项目 ----
            var store = ProjectStore.Create(tempRoot, "测试项目");
            Check("Create: 项目目录与存档库存在", ProjectStore.IsProjectDir(store.ProjectDir));

            // ---- 写入初始文件（含中文文件名）----
            Directory.CreateDirectory(Path.Combine(store.ProjectDir, "data"));
            File.WriteAllText(Path.Combine(store.ProjectDir, "readme.txt"), "版本 1");
            File.WriteAllText(Path.Combine(store.ProjectDir, "data", "save.txt"), "生命值: 100");
            File.WriteAllText(Path.Combine(store.ProjectDir, "存档说明.txt"), "中文文件名测试");

            // ---- 第一次存档 ----
            var s1 = store.Save("测试项目 初始存档", isAuto: false, progress: null);
            Check("Save: 文件数=3", s1.FileCount == 3);
            Check("Save: 成为当前存档", store.CurrentSave?.Id == s1.Id);

            // ---- 改动：修改 + 新增 + 删除 ----
            File.WriteAllText(Path.Combine(store.ProjectDir, "readme.txt"), "版本 2");
            File.WriteAllText(Path.Combine(store.ProjectDir, "new.txt"), "新增文件");
            File.Delete(Path.Combine(store.ProjectDir, "data", "save.txt"));
            var st = store.GetStatus();
            Check("Status: 1 修改 + 1 新增 + 1 删除",
                st.Modified.Count == 1 && st.Added.Count == 1 && st.Deleted.Count == 1);

            // ---- 读档（带自动恢复）----
            var result = store.Load(s1, autoRecover: true, progress: null);
            Check("Load: 自动恢复存档已创建", result.Recovered != null);
            Check("Load: 存档列表=2", store.ListSaves().Count == 2);
            Check("Load: readme 回到版本 1", File.ReadAllText(Path.Combine(store.ProjectDir, "readme.txt")) == "版本 1");
            Check("Load: 新增文件被移除", !File.Exists(Path.Combine(store.ProjectDir, "new.txt")));
            Check("Load: 删除文件被还原", File.Exists(Path.Combine(store.ProjectDir, "data", "save.txt")));
            Check("Load: 还原后无未保存改动", !store.GetStatus().Dirty);

            // ---- 重命名 / 删除 ----
            store.RenameSave(s1, "改名后的存档");
            Check("Rename: 名称已更新", store.ListSaves().First(x => x.Id == s1.Id).Name == "改名后的存档");

            store.DeleteSave(s1, progress: null);
            Check("Delete: 列表剩 1 个", store.ListSaves().Count == 1);
            Check("Delete: 当前存档切换为自动恢复档", store.CurrentSave?.IsAuto == true);

            // ---- 校验规则 ----
            Check("Validate: 非法字符拒绝", Throws(() => ProjectStore.Create(tempRoot, "a<b>")));
            Check("Validate: 空名拒绝", Throws(() => ProjectStore.Create(tempRoot, "  ")));
            Check("Validate: 已存在拒绝", Throws(() => ProjectStore.Create(tempRoot, "测试项目")));

            // ---- 打开 / 列出项目 ----
            var projects = ProjectStore.ListProjects(tempRoot);
            Check("ListProjects: 找到 1 个", projects.Count == 1 && projects[0].Name == "测试项目");
            var reopened = ProjectStore.Open(projects[0].Dir);
            Check("Open: 重开项目", reopened.ProjectName == "测试项目");

            // ---- 清单越界防护：篡改 manifest 后读档必须拒绝，且不动工作区 ----
            var cur = reopened.ListSaves().First();
            string manifestPath = Path.Combine(reopened.ProjectDir, ProjectStore.StoreDirName, "saves", cur.Id, "manifest.json");
            File.WriteAllText(manifestPath,
                """{"Files":[{"Path":"../evil.txt","Size":1,"UtcTicks":1,"Sha256":"aa"}],"Directories":[]}""");
            Check("Manifest: '..' 越界路径被拒绝", Throws(() => reopened.Load(cur, autoRecover: false, progress: null)));
            File.WriteAllText(manifestPath,
                """{"Files":[{"Path":"C:/evil.txt","Size":1,"UtcTicks":1,"Sha256":"aa"}],"Directories":[]}""");
            Check("Manifest: 绝对路径被拒绝", Throws(() => reopened.Load(cur, autoRecover: false, progress: null)));

            // ---- 空项目存档 / 删项目 ----
            var empty = ProjectStore.Create(tempRoot, "空项目");
            var es = empty.Save("空存档", isAuto: false, progress: null);
            Check("Save: 空项目 0 文件", es.FileCount == 0);
            Check("Status: 空项目无改动", !empty.GetStatus().Dirty);
            ProjectStore.DeleteProjectAt(empty.ProjectDir);
            Check("DeleteProject: 目录已移除", !Directory.Exists(empty.ProjectDir));

            // ---- 导入已有目录（原地纳入管理）----
            string extDir = Path.Combine(tempRoot, "外部项目");
            Directory.CreateDirectory(Path.Combine(extDir, "src"));
            File.WriteAllText(Path.Combine(extDir, "src", "main.txt"), "外部代码 v1");
            File.WriteAllText(Path.Combine(extDir, "说明.txt"), "外部项目的说明");
            var adopted = ProjectStore.Adopt(extDir);
            Check("Adopt: 目录成为项目", ProjectStore.IsProjectDir(extDir));
            Check("Adopt: 项目名=目录名", adopted.ProjectName == "外部项目");
            Check("Adopt: 现有文件未被改动", File.ReadAllText(Path.Combine(extDir, "src", "main.txt")) == "外部代码 v1");
            var asp = adopted.Save("导入存档", isAuto: false, progress: null);
            Check("Adopt: 首存 2 文件", asp.FileCount == 2);
            Check("Adopt: 再次导入被拒绝", Throws(() => ProjectStore.Adopt(extDir)));
            Check("Adopt: 磁盘根目录被拒绝", Throws(() => ProjectStore.Adopt(Path.GetPathRoot(tempRoot)!)));
            Check("Adopt: 不存在目录被拒绝", Throws(() => ProjectStore.Adopt(Path.Combine(tempRoot, "不存在"))));
            var reload = ProjectStore.Open(extDir);
            Check("Adopt: 可重新打开", reload.ProjectName == "外部项目" && reload.CurrentSave?.Id == asp.Id);
            ProjectStore.DeleteProjectAt(extDir);
            Check("Adopt: 导入项目可整体删除", !Directory.Exists(extDir));

            // ---- Unity 项目：自动排除重建目录 ----
            string uniDir = Path.Combine(tempRoot, "UnityDemo");
            Directory.CreateDirectory(Path.Combine(uniDir, "Assets"));
            Directory.CreateDirectory(Path.Combine(uniDir, "ProjectSettings"));
            Directory.CreateDirectory(Path.Combine(uniDir, "Library"));
            File.WriteAllText(Path.Combine(uniDir, "Assets", "Scene.unity"), "unity v1");
            File.WriteAllText(Path.Combine(uniDir, "Library", "ArtifactDB"), "超大缓存数据");
            File.WriteAllText(Path.Combine(uniDir, "ProjectSettings", "ProjectSettings.asset"), "settings");
            var uni = ProjectStore.Adopt(uniDir);
            Check("Unity: 识别并自动排除重建目录",
                ProjectStore.UnityDefaultExcludes.All(n => uni.ExcludeDirs.Contains(n, StringComparer.OrdinalIgnoreCase)));
            var usp = uni.Save("u1", isAuto: false, progress: null);
            Check("Unity: Library 未入档", usp.FileCount == 2); // Assets/Scene.unity + ProjectSettings.asset
            File.WriteAllText(Path.Combine(uniDir, "Assets", "Scene.unity"), "unity v2");
            uni.Load(usp, autoRecover: false, progress: null);
            Check("Unity: 读档还原源文件", File.ReadAllText(Path.Combine(uniDir, "Assets", "Scene.unity")) == "unity v1");
            Check("Unity: Library 读档后原样保留", Directory.Exists(Path.Combine(uniDir, "Library"))
                && File.ReadAllText(Path.Combine(uniDir, "Library", "ArtifactDB")) == "超大缓存数据");
            Check("Unity: 排除后状态无改动", !uni.GetStatus().Dirty);

            // 排除目录读档语义：不删除、不还原、原样保留（用户数据安全）
            uni.SetExcludeDirs(ProjectStore.UnityDefaultExcludes.Append("ProjectSettings"));
            File.WriteAllText(Path.Combine(uniDir, "ProjectSettings", "ProjectSettings.asset"), "用户新设置");
            File.WriteAllText(Path.Combine(uniDir, "Assets", "Scene.unity"), "unity v3");
            var s3 = uni.Save("s3", isAuto: false, progress: null);
            Check("Exclude: 排除 ProjectSettings 后存档仅 1 文件", s3.FileCount == 1);
            uni.Load(s3, autoRecover: false, progress: null);
            Check("Exclude: 读档还原 Assets", File.ReadAllText(Path.Combine(uniDir, "Assets", "Scene.unity")) == "unity v3");
            Check("Exclude: 读档保留 ProjectSettings 用户改动",
                File.ReadAllText(Path.Combine(uniDir, "ProjectSettings", "ProjectSettings.asset")) == "用户新设置");

            uni.SetExcludeDirs(new[] { "Assets" });
            Check("Exclude: 自定义排除生效", uni.ExcludeDirs.Count == 1 && uni.ExcludeDirs[0] == "Assets");
            var xsp = uni.Save("x1", isAuto: false, progress: null);
            Check("Exclude: 只排除 Assets 时存 2 文件", xsp.FileCount == 2); // ProjectSettings.asset + Library/ArtifactDB 重新入档
            Check("Exclude: 非法目录名拒绝", Throws(() => uni.SetExcludeDirs(new[] { ".." })));
            Check("Exclude: 含分隔符拒绝", Throws(() => uni.SetExcludeDirs(new[] { "a/b" })));
            uni.SetExcludeDirs(new[] { ProjectStore.StoreDirName });
            Check("Exclude: .mygit 被忽略", uni.ExcludeDirs.Count == 0);

            // ---- Unity：只导入 Assets（外部存档库，Unity 看不见）----
            string projRoot = Path.Combine(tempRoot, "UnityProj");
            string assetsDir = Path.Combine(projRoot, "Assets");
            Directory.CreateDirectory(assetsDir);
            Directory.CreateDirectory(Path.Combine(projRoot, "ProjectSettings"));
            Directory.CreateDirectory(Path.Combine(assetsDir, ".mygit-x")); // 嵌套存档库应被前缀规则跳过
            File.WriteAllText(Path.Combine(assetsDir, ".mygit-x", "evil.txt"), "嵌套存档库内容");
            File.WriteAllText(Path.Combine(assetsDir, "Model.prefab"), "prefab v1");
            var ext = ProjectStore.AdoptExternal(assetsDir);
            Check("External: 存档库在 Assets 外", !ext.StoreDir.StartsWith(ext.ProjectDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
            Check("External: 存档库名 .mygit-Assets", Path.GetFileName(ext.StoreDir) == ".mygit-Assets");
            Check("External: 项目名 UnityProj/Assets", ext.ProjectName == "UnityProj/Assets");
            Check("External: 已受管理", ProjectStore.IsManagedDir(assetsDir) && !ProjectStore.IsProjectDir(assetsDir));
            var esp = ext.Save("e1", isAuto: false, progress: null);
            Check("External: 嵌套 .mygit- 目录被跳过", esp.FileCount == 1); // 只 Model.prefab
            File.WriteAllText(Path.Combine(assetsDir, "Model.prefab"), "prefab v2");
            ext.Load(esp, autoRecover: false, progress: null);
            Check("External: 读档还原", File.ReadAllText(Path.Combine(assetsDir, "Model.prefab")) == "prefab v1");
            var reopenedExt = ProjectStore.Open(assetsDir);
            Check("External: 可按目录重新打开", reopenedExt.IsExternal && reopenedExt.ProjectName == "UnityProj/Assets");
            Check("External: 重复外部导入被拒绝", Throws(() => ProjectStore.AdoptExternal(assetsDir)));
            ProjectStore.DeleteProjectAt(assetsDir);
            Check("External: 删除只动存档库，Assets 原样保留",
                !Directory.Exists(Path.Combine(projRoot, ".mygit-Assets"))
                && File.Exists(Path.Combine(assetsDir, "Model.prefab")));
        }
        catch (Exception ex)
        {
            fail++;
            log.AppendLine($"[FAIL] 未预期异常: {ex.GetType().Name}: {ex.Message}");
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempRoot)) Directory.Delete(tempRoot, true);
            }
            catch (IOException)
            {
                // 临时目录清理失败（可能被占用）：不影响自检结果
            }
            catch (UnauthorizedAccessException)
            {
                // 同上：清理失败不影响结果
            }
        }

        log.AppendLine(fail == 0 ? "== 全部通过 ==" : $"== {fail} 项失败 ==");
        string text = log.ToString();
        Console.WriteLine(text);
        try { File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "selftest.log"), text); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 日志文件写不进程序目录时 stdout 仍有效
        }
        return fail == 0;
    }

    /// <summary>执行 action，抛出任何异常返回 true（预期抛出的场景）。</summary>
    private static bool Throws(Action action)
    {
        try { action(); return false; }
        catch { return true; } // 预期抛出即通过
    }
}
