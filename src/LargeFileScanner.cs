Exit code: 0
Wall time: 1 seconds
Output:
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;
using System.Windows.Forms;

namespace LargeFileScanner
{
    static class Program
    {
        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    public sealed class FileRow
    {
        public bool Selected { get; set; }
        public long Bytes { get; set; }
        public string Size { get; set; }
        public DateTime Modified { get; set; }
        public string Category { get; set; }
        public string Path { get; set; }
    }

    public sealed class MainForm : Form
    {
        readonly ComboBox driveBox = new ComboBox();
        readonly NumericUpDown thresholdBox = new NumericUpDown();
        readonly Button scanButton = new Button();
        readonly Button cancelButton = new Button();
        readonly Button deleteButton = new Button();
        readonly Button forceDeleteButton = new Button();
        readonly Button openButton = new Button();
        readonly Button exportButton = new Button();
        readonly Button helpButton = new Button();
        readonly DataGridView grid = new DataGridView();
        readonly Label status = new Label();
        readonly ProgressBar progress = new ProgressBar();
        readonly BindingList<FileRow> rows = new BindingList<FileRow>();
        CancellationTokenSource cancelSource;

        public MainForm()
        {
            Text = "本地大文件扫描器";
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            Width = 1120; Height = 700; MinimumSize = new Size(850, 500);
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Microsoft YaHei UI", 9F);
            BuildUi();
            LoadDrives();
        }

        void BuildUi()
        {
            var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 52, Padding = new Padding(10, 10, 6, 6), WrapContents = false };
            top.Controls.Add(new Label { Text = "扫描盘：", AutoSize = true, Margin = new Padding(3, 7, 2, 0) });
            driveBox.DropDownStyle = ComboBoxStyle.DropDownList; driveBox.Width = 90;
            top.Controls.Add(driveBox);
            top.Controls.Add(new Label { Text = "最小文件：", AutoSize = true, Margin = new Padding(14, 7, 2, 0) });
            thresholdBox.Minimum = 1; thresholdBox.Maximum = 102400; thresholdBox.Value = 500; thresholdBox.Width = 90;
            top.Controls.Add(thresholdBox);
            top.Controls.Add(new Label { Text = "MB", AutoSize = true, Margin = new Padding(2, 7, 10, 0) });
            scanButton.Text = "开始扫描"; scanButton.AutoSize = true; scanButton.Click += StartScan;
            cancelButton.Text = "取消"; cancelButton.AutoSize = true; cancelButton.Enabled = false; cancelButton.Click += delegate { if (cancelSource != null) cancelSource.Cancel(); };
            deleteButton.Text = "删除勾选"; deleteButton.AutoSize = true; deleteButton.Enabled = false; deleteButton.Click += DeleteSelected;
            forceDeleteButton.Text = "管理员强制删除"; forceDeleteButton.AutoSize = true; forceDeleteButton.Enabled = false; forceDeleteButton.Click += ForceDeleteSelected;
            openButton.Text = "打开所在位置"; openButton.AutoSize = true; openButton.Enabled = false; openButton.Click += OpenLocation;
            exportButton.Text = "导出清单"; exportButton.AutoSize = true; exportButton.Enabled = false; exportButton.Click += ExportCsv;
            helpButton.Text = "使用说明"; helpButton.AutoSize = true; helpButton.Click += ShowHelp;
            top.Controls.Add(scanButton); top.Controls.Add(cancelButton); top.Controls.Add(deleteButton); top.Controls.Add(forceDeleteButton); top.Controls.Add(openButton); top.Controls.Add(exportButton); top.Controls.Add(helpButton);

            grid.Dock = DockStyle.Fill; grid.AutoGenerateColumns = false; grid.DataSource = rows;
            grid.AllowUserToAddRows = false; grid.AllowUserToDeleteRows = false; grid.MultiSelect = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect; grid.RowHeadersVisible = false;
            grid.Columns.Add(new DataGridViewCheckBoxColumn { DataPropertyName = "Selected", HeaderText = "选", Width = 42 });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Size", HeaderText = "大小", Width = 90, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Modified", HeaderText = "修改时间", Width = 145, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Category", HeaderText = "建议", Width = 145, ReadOnly = true });
            grid.Columns.Add(new DataGridViewTextBoxColumn { DataPropertyName = "Path", HeaderText = "完整路径", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, ReadOnly = true });
            grid.CellFormatting += delegate(object s, DataGridViewCellFormattingEventArgs e) {
                if (e.RowIndex >= 0 && rows[e.RowIndex].Category.StartsWith("系统")) grid.Rows[e.RowIndex].DefaultCellStyle.BackColor = Color.MistyRose;
                else if (e.RowIndex >= 0 && rows[e.RowIndex].Category.Contains("缓存")) grid.Rows[e.RowIndex].DefaultCellStyle.BackColor = Color.Honeydew;
            };
            grid.SelectionChanged += delegate { openButton.Enabled = grid.CurrentRow != null; };

            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 54, Padding = new Padding(10, 7, 10, 6) };
            progress.Dock = DockStyle.Top; progress.Height = 5; progress.Style = ProgressBarStyle.Marquee; progress.Visible = false;
            status.Dock = DockStyle.Fill; status.Text = "就绪。扫描只读取文件信息，不会自动删除。"; status.Padding = new Padding(0, 10, 0, 0);
            bottom.Controls.Add(status); bottom.Controls.Add(progress);
            Controls.Add(grid); Controls.Add(bottom); Controls.Add(top);
        }

        void LoadDrives()
        {
            foreach (var d in DriveInfo.GetDrives().Where(x => x.IsReady && x.DriveType == DriveType.Fixed)) driveBox.Items.Add(d.Name);
            if (driveBox.Items.Count > 0) driveBox.SelectedIndex = 0;
        }

        async void StartScan(object sender, EventArgs e)
        {
            if (driveBox.SelectedItem == null) return;
            rows.Clear(); ToggleScanning(true); cancelSource = new CancellationTokenSource();
            string root = driveBox.SelectedItem.ToString(); long minBytes = (long)thresholdBox.Value * 1024L * 1024L;
            status.Text = "正在扫描 " + root + "，请稍候……";
            var reporter = new Progress<string>(p => status.Text = "正在扫描：" + p);
            try
            {
                var found = await System.Threading.Tasks.Task.Run(() => Scan(root, minBytes, cancelSource.Token, reporter));
                foreach (var f in found.OrderByDescending(x => x.Bytes)) rows.Add(f);
                long total = found.Sum(x => x.Bytes);
                status.Text = string.Format("完成：找到 {0} 个文件，合计 {1}。勾选删除前请确认文件用途。", found.Count, FormatSize(total));
            }
            catch (OperationCanceledException) { status.Text = "扫描已取消。"; }
            catch (Exception ex) { MessageBox.Show("扫描失败：" + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error); status.Text = "扫描失败。"; }
            finally { ToggleScanning(false); cancelSource.Dispose(); cancelSource = null; }
        }

        static List<FileRow> Scan(string root, long minBytes, CancellationToken token, IProgress<string> reporter)
        {
            var result = new List<FileRow>(); var stack = new Stack<string>(); stack.Push(root); int seen = 0;
            while (stack.Count > 0)
            {
                token.ThrowIfCancellationRequested(); string dir = stack.Pop();
                if ((++seen % 250) == 0) reporter.Report(dir);
                try
                {
                    foreach (string file in Directory.EnumerateFiles(dir))
                    {
                        token.ThrowIfCancellationRequested();
                        try { var fi = new FileInfo(file); if (fi.Length >= minBytes) result.Add(new FileRow { Bytes = fi.Length, Size = FormatSize(fi.Length), Modified = fi.LastWriteTime, Category = Classify(fi.FullName), Path = fi.FullName }); } catch { }
                    }
                } catch { }
                try
                {
                    foreach (string sub in Directory.EnumerateDirectories(dir))
                    {
                        try { var a = new DirectoryInfo(sub).Attributes; if ((a & FileAttributes.ReparsePoint) == 0) stack.Push(sub); } catch { }
                    }
                } catch { }
            }
            return result;
        }

        static string Classify(string path)
        {
            string p = path.ToLowerInvariant();
            if (p.Contains("\\windows\\") || p.Contains("\\program files\\") || p.Contains("\\program files (x86)\\") || p.EndsWith("pagefile.sys") || p.EndsWith("swapfile.sys") || p.EndsWith("hiberfil.sys")) return "系统/程序文件（勿删）";
            if (p.Contains("\\temp\\") || p.Contains("\\cache\\") || p.Contains("\\.cache\\") || p.Contains("\\dataline\\.tmp\\")) return "缓存候选（确认后可删）";
            if (p.EndsWith(".apk") || p.EndsWith(".exe") || p.EndsWith(".msi")) return "安装包（确认后可删）";
            if (p.EndsWith(".log")) return "日志（谨慎清理）";
            return "用户文件（先确认）";
        }

        void ToggleScanning(bool active)
        {
            scanButton.Enabled = !active; driveBox.Enabled = !active; thresholdBox.Enabled = !active; cancelButton.Enabled = active;
            deleteButton.Enabled = !active && rows.Count > 0; forceDeleteButton.Enabled = !active && rows.Count > 0; exportButton.Enabled = !active && rows.Count > 0; progress.Visible = active;
        }

        static bool Protected(string path)
        {
            string p = Path.GetFullPath(path).ToLowerInvariant();
            string tail = p.Length > 3 ? p.Substring(3) : "";
            bool protectedDir = tail.StartsWith("windows\\") || tail.StartsWith("program files\\") || tail.StartsWith("program files (x86)\\") || tail.StartsWith("programdata\\");
            bool systemFile = p.EndsWith("pagefile.sys") || p.EndsWith("swapfile.sys") || p.EndsWith("hiberfil.sys") || p.EndsWith("dumpstack.log") || p.EndsWith("dumpstack.log.tmp");
            bool mumuDisk = p.Contains("\\mumu\\") && (p.EndsWith(".vdi") || p.EndsWith(".vmdk") || p.EndsWith(".img") || p.Contains("\\system"));
            return protectedDir || systemFile || mumuDisk;
        }

        static bool IsAdministrator()
        {
            try { return new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator); }
            catch { return false; }
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern bool MoveFileEx(string existingFile, string newFile, int flags);
        const int MOVEFILE_DELAY_UNTIL_REBOOT = 4;

        void ForceDeleteSelected(object sender, EventArgs e)
        {
            grid.EndEdit(); var chosen = rows.Where(x => x.Selected).ToList();
            if (chosen.Count == 0) { MessageBox.Show("请先勾选要强制删除的文件。", "提示"); return; }
            var blocked = chosen.Where(x => Protected(x.Path)).ToList();
            if (blocked.Count > 0)
            {
                MessageBox.Show("为防止系统、程序或模拟器损坏，以下目标禁止强制删除：\n\n" + string.Join("\n", blocked.Select(x => x.Path).ToArray()), "安全保护已阻止", MessageBoxButtons.OK, MessageBoxIcon.Stop);
                return;
            }
            if (!IsAdministrator())
            {
                if (MessageBox.Show("强制删除需要管理员权限。\n\n是否立即以管理员身份重新启动工具？重新启动后请重新扫描并勾选文件。", "需要管理员权限", MessageBoxButtons.YesNo, MessageBoxIcon.Information, MessageBoxDefaultButton.Button1) == DialogResult.Yes)
                {
                    try { Process.Start(new ProcessStartInfo { FileName = Application.ExecutablePath, UseShellExecute = true, Verb = "runas" }); Application.Exit(); }
                    catch (Exception ex) { MessageBox.Show("无法取得管理员权限：" + ex.Message, "未提升权限", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
                }
                return;
            }
            long total = chosen.Sum(x => x.Bytes);
            if (MessageBox.Show(string.Format("管理员强制删除将修改文件权限并直接删除 {0} 个文件，共 {1}。\n\n删除不会进入回收站，无法撤销。确定继续吗？", chosen.Count, FormatSize(total)), "高风险操作确认", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

            int deleted = 0, scheduled = 0; long freed = 0; var errors = new List<string>();
            foreach (var item in chosen)
            {
                try
                {
                    if (!File.Exists(item.Path)) { rows.Remove(item); continue; }
                    try { File.SetAttributes(item.Path, FileAttributes.Normal); } catch { }
                    RunHidden("takeown.exe", "/F \"" + item.Path + "\" /A");
                    RunHidden("icacls.exe", "\"" + item.Path + "\" /grant *S-1-5-32-544:F /C");
                    try { File.Delete(item.Path); }
                    catch
                    {
                        if (MoveFileEx(item.Path, null, MOVEFILE_DELAY_UNTIL_REBOOT)) scheduled++;
                        else throw;
                    }
                    if (!File.Exists(item.Path)) { deleted++; freed += item.Bytes; rows.Remove(item); }
                    else if (scheduled > 0) item.Selected = false;
                }
                catch (Exception ex) { errors.Add(item.Path + "：" + ex.Message); }
            }
            grid.Refresh();
            status.Text = string.Format("强制删除完成：立即删除 {0} 个（{1}），安排重启后删除 {2} 个。", deleted, FormatSize(freed), scheduled);
            string summary = string.Format("立即删除：{0} 个\n安排重启后删除：{1} 个\n\n重启后删除的文件当前仍会显示，Windows 下次启动时处理。", deleted, scheduled);
            if (errors.Count > 0) summary += "\n\n仍失败：\n" + string.Join("\n", errors.ToArray());
            MessageBox.Show(summary, errors.Count == 0 ? "强制删除结果" : "部分失败", MessageBoxButtons.OK, errors.Count == 0 ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
        }

        static void RunHidden(string file, string arguments)
        {
            try
            {
                using (var p = Process.Start(new ProcessStartInfo { FileName = file, Arguments = arguments, UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden }))
                { if (p != null) p.WaitForExit(15000); }
            }
            catch { }
        }

        void DeleteSelected(object sender, EventArgs e)
        {
            grid.EndEdit(); var chosen = rows.Where(x => x.Selected).ToList();
            if (chosen.Count == 0) { MessageBox.Show("请先勾选要删除的文件。", "提示"); return; }
            var blocked = chosen.Where(x => Protected(x.Path)).ToList();
            if (blocked.Count > 0) { MessageBox.Show("包含系统或程序目录文件，工具拒绝删除：\n\n" + string.Join("\n", blocked.Select(x => x.Path).ToArray()), "已阻止", MessageBoxButtons.OK, MessageBoxIcon.Warning); return; }
            long total = chosen.Sum(x => x.Bytes);
            if (MessageBox.Show(string.Format("将直接删除 {0} 个文件，共 {1}。\n\n不会进入回收站，且无法撤销。确定继续吗？", chosen.Count, FormatSize(total)), "确认删除", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            int deleted = 0; long freed = 0; var errors = new List<string>();
            foreach (var item in chosen)
            {
                try { if (File.Exists(item.Path)) { File.Delete(item.Path); freed += item.Bytes; } rows.Remove(item); deleted++; }
                catch (Exception ex) { errors.Add(item.Path + "：" + ex.Message); }
            }
            status.Text = string.Format("已删除 {0} 个文件，释放约 {1}。", deleted, FormatSize(freed));
            if (errors.Count > 0) MessageBox.Show("以下文件删除失败：\n\n" + string.Join("\n", errors.ToArray()), "部分失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        void OpenLocation(object sender, EventArgs e)
        {
            if (grid.CurrentRow == null) return; var item = grid.CurrentRow.DataBoundItem as FileRow; if (item == null) return;
            try { Process.Start("explorer.exe", "/select,\"" + item.Path + "\""); } catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        void ExportCsv(object sender, EventArgs e)
        {
            using (var dlg = new SaveFileDialog { Filter = "CSV 文件|*.csv", FileName = "大文件清单.csv" })
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                try
                {
                    using (var sw = new StreamWriter(dlg.FileName, false, new System.Text.UTF8Encoding(true)))
                    {
                        sw.WriteLine("大小,字节数,修改时间,建议,完整路径");
                        foreach (var x in rows) sw.WriteLine(string.Join(",", new[] { Csv(x.Size), x.Bytes.ToString(), Csv(x.Modified.ToString("yyyy-MM-dd HH:mm:ss")), Csv(x.Category), Csv(x.Path) }));
                    }
                    status.Text = "清单已导出：" + dlg.FileName;
                } catch (Exception ex) { MessageBox.Show("导出失败：" + ex.Message); }
            }
        }

        void ShowHelp(object sender, EventArgs e)
        {
            string text =
                "【扫描】\n" +
                "1. 选择盘符并设置最小文件大小，默认 500 MB。\n" +
                "2. 点击“开始扫描”，结果会按大小从大到小显示。\n" +
                "3. 绿色通常是缓存候选，红色是系统或程序文件。\n\n" +
                "【查看与导出】\n" +
                "选中一行可打开文件所在位置；“导出清单”可保存 CSV。\n\n" +
                "【普通删除】\n" +
                "勾选文件后点击“删除勾选”。文件会直接删除，不进入回收站。\n\n" +
                "【管理员强制删除】\n" +
                "仅在普通删除提示访问被拒绝时使用。工具会申请管理员权限、修复目标文件权限；被占用的文件可安排重启后删除。\n\n" +
                "【安全保护】\n" +
                "工具禁止删除 Windows、Program Files、ProgramData、分页/休眠文件，以及 MuMu 的虚拟磁盘和系统镜像。\n" +
                "请勿用本工具代替软件卸载。删除前务必核对完整路径。";
            MessageBox.Show(text, "本地大文件扫描器－使用说明", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        static string Csv(string value) { return "\"" + (value ?? "").Replace("\"", "\"\"") + "\""; }
        static string FormatSize(long bytes)
        {
            if (bytes >= 1024L * 1024L * 1024L) return (bytes / 1073741824.0).ToString("0.###") + " GB";
            return (bytes / 1048576.0).ToString("0.0") + " MB";
        }
    }
}

