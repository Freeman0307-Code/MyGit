using System.Drawing.Imaging;

namespace MyGit;

internal static class Program
{
    /// <summary>
    /// 启动参数：
    ///   （无）            正常打开主界面
    ///   --selftest       跑一遍快照引擎自检后退出（结果写 stdout 与 selftest.log）
    ///   --demo           启动时创建演示项目并打开
    ///   --shot <路径>     启动后自动截图保存并退出（配合 --demo 生成界面预览图）
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            Environment.ExitCode = SelfTest.Run() ? 0 : 1;
            return;
        }

        ApplicationConfiguration.Initialize();

        bool wantsShot = args.Any(a => a.Equals("--shot", StringComparison.OrdinalIgnoreCase));
        string shotPath = ArgValue(args, "--shot") ?? "preview.png";

        var form = new MyGitForm(args);
        if (!wantsShot)
        {
            Application.Run(form);
            return;
        }

        form.Shown += async (_, _) =>
        {
            await Task.Delay(700); // 等日志输出与首帧绘制完成
            try
            {
                string full = Path.GetFullPath(shotPath);
                string? dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                using var bmp = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                bmp.Save(full, ImageFormat.Png);
                Console.WriteLine("screenshot saved: " + full);
            }
            catch (Exception ex)
            {
                Console.WriteLine("screenshot failed: " + ex.Message);
            }
            Application.Exit();
        };
        Application.Run(form);
    }

    /// <summary>读取 --name 后的参数值（没有则返回 null）。</summary>
    private static string? ArgValue(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        return null;
    }
}
