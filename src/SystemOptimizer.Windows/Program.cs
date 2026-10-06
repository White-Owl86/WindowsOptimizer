using System.Diagnostics;
using System.Collections.Concurrent;
using System.Security.Principal;
using Microsoft.Win32;
using SystemOptimizer.Core;

namespace SystemOptimizerApp;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var identity = WindowsIdentity.GetCurrent();
        if (!new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator))
        {
            MessageBox.Show("برنامه را با Run as administrator اجرا کنید.", "دسترسی لازم", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        Application.Run(new MainForm());
    }
}

internal sealed class MainForm : Form
{
    private readonly Dictionary<Optimization, CheckBox> options = [];
    private readonly CheckBox closeFirefox = new() { Text = "درخواست بستن عادی Firefox پس از ثبت سیاست (ذخیرهٔ کارها پیش از اجرا)", AutoSize = true, Enabled = false };
    private readonly Button start = new() { Text = "اجرای گزینه‌های انتخاب‌شده", Dock = DockStyle.Fill, Height = 40 };
    private readonly TextBox log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, RightToLeft = RightToLeft.No, WordWrap = false };
    private readonly ProgressBar progress = new() { Dock = DockStyle.Fill, Maximum = 100 };
    private bool running;

    public MainForm()
    {
        Text = "Windows Optimizer 5 — بهینه‌ساز ویندوز";
        MinimumSize = new Size(720, 680);
        Size = new Size(840, 870);
        StartPosition = FormStartPosition.CenterScreen;
        RightToLeft = RightToLeft.Yes;
        RightToLeftLayout = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Tahoma", 9F);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 1, RowCount = 6 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 22));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        layout.Controls.Add(new Label { Text = "تغییرات انتخابی ویندوز — نتیجهٔ هر عملیات جداگانه گزارش می‌شود", AutoSize = true, Margin = new Padding(0, 0, 0, 10) }, 0, 0);
        string cpu = "نام پردازنده در دسترس نیست";
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            cpu = key?.GetValue("ProcessorNameString")?.ToString()?.Trim() ?? cpu;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException) { }
        layout.Controls.Add(new Label {
            Text = $"{Environment.OSVersion} | {cpu}\nحساب اجرای تنظیمات: {Environment.UserDomainName}\\{Environment.UserName}",
            AutoSize = true, MaximumSize = new Size(750, 0), Margin = new Padding(0, 0, 0, 10)
        }, 0, 1);
        var choices = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
        AddOption(choices, Optimization.Services, "غیرفعال‌کردن SysMain، Windows Search و DiagTrack");
        AddOption(choices, Optimization.Power, "فعال‌کردن High Performance در صورت وجود این طرح روی دستگاه");
        AddOption(choices, Optimization.Updates, "توقف و غیرفعال‌کردن سرویس‌های Windows Update و Delivery Optimization");
        AddOption(choices, Optimization.HungApps, "درخواست بستن عادی Photos یا GameBar فقط در صورت پاسخ‌ندادن");
        AddOption(choices, Optimization.Firefox, "افزودن تخلیهٔ تب در کمبود حافظه به سیاست Firefox با حفظ تنظیمات قبلی");
        choices.Controls.Add(closeFirefox);
        options[Optimization.Firefox].CheckedChanged += (_, _) => {
            closeFirefox.Enabled = options[Optimization.Firefox].Checked && !running;
            if (!options[Optimization.Firefox].Checked) closeFirefox.Checked = false;
        };
        AddOption(choices, Optimization.Tasks, "غیرفعال‌کردن شش تسک مشخص تله‌متری مایکروسافت");
        AddOption(choices, Optimization.Visuals, "خاموش‌کردن شفافیت و ثبت ترجیح کاهش جلوه‌ها برای حساب فعلی");
        AddOption(choices, Optimization.Browsers, "ثبت سیاست Widgets و Memory Saver / Sleeping Tabs مرورگرها");
        AddOption(choices, Optimization.Pagefile, "فعال‌کردن مدیریت خودکار Pagefile توسط ویندوز");
        AddOption(choices, Optimization.Temp, "پاکسازی فایل‌های Temp قدیمی‌تر از ۷ روز (بدون کش آپدیت و Crash Dump)");
        layout.Controls.Add(choices, 0, 2);
        layout.Controls.Add(start, 0, 3);
        layout.Controls.Add(progress, 0, 4);
        layout.Controls.Add(log, 0, 5);
        Controls.Add(layout);
        start.Click += Start_Click;
        FormClosing += (_, e) => {
            if (!running) return;
            e.Cancel = true;
            MessageBox.Show(this, "عملیات در حال اجراست؛ تا پایان و ثبت نتیجه صبر کنید.", "عملیات جاری", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
    }

    private void AddOption(FlowLayoutPanel parent, Optimization key, string text)
    {
        var check = new CheckBox { Text = text, AutoSize = true, Checked = false, Margin = new Padding(3, 6, 3, 6) };
        options.Add(key, check);
        parent.Controls.Add(check);
    }

    private void AppendLog(string message) => log.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");

    private async void Start_Click(object? sender, EventArgs e)
    {
        if (running) return;
        var selected = options.Where(pair => pair.Value.Checked).ToList();
        if (selected.Count == 0)
        {
            MessageBox.Show(this, "حداقل یک گزینه را انتخاب کنید.", "انتخاب عملیات", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        string warning = "این تغییرات ممکن است بخشی از قابلیت‌های ویندوز را محدود کنند. افزایش سرعت تضمین نمی‌شود.\n\n" +
            string.Join("\n", selected.Select(pair => "• " + pair.Value.Text));
        if (selected.Any(pair => pair.Key == Optimization.Services)) warning += "\n\nWindows Search غیرفعال می‌شود و جست‌وجو ممکن است کندتر شود.";
        if (selected.Any(pair => pair.Key == Optimization.Updates)) warning += "\n\nدریافت خودکار به‌روزرسانی‌های امنیتی مختل می‌شود؛ ویندوز یا سیاست سازمان ممکن است سرویس را دوباره فعال کند.";
        if (selected.Any(pair => pair.Key == Optimization.Power)) warning += "\n\nمصرف باتری و گرما ممکن است بیشتر شود.";
        if (selected.Any(pair => pair.Key == Optimization.Temp)) warning += "\n\nفایل‌های موقت قدیمی حذف می‌شوند و بازگردانی خودکار ندارند.";
        if (closeFirefox.Checked || selected.Any(pair => pair.Key == Optimization.HungApps)) warning += "\n\nکارهای ذخیره‌نشده را ذخیره کنید. حفظ تب‌های Firefox تضمین نمی‌شود؛ بازکردن دوبارهٔ آن دستی است.";
        warning += "\n\nاگر یک مرحله شکست بخورد، تغییرات مراحل قبلی برگردانده نمی‌شوند. ادامه می‌دهید؟";
        if (MessageBox.Show(this, warning, "تأیید تغییرات", MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        running = true;
        start.Enabled = false;
        foreach (var check in options.Values) check.Enabled = false;
        bool shouldCloseFirefox = closeFirefox.Checked;
        closeFirefox.Enabled = false;
        log.Clear();
        progress.Value = 0;
        string? logPath = null;
        try
        {
            var messages = new Progress<string>(AppendLog);
            var details = new ConcurrentQueue<string>();
            var updates = new Progress<OperationProgress>(update => {
                progress.Value = update.Total == 0 ? 0 : update.Completed * 100 / update.Total;
                AppendLog($"[{update.Result.Status}] {update.Result.Name}\r\n{update.Result.Detail}");
            });
            var executor = new WindowsOperations(message => {
                details.Enqueue(message);
                ((IProgress<string>)messages).Report(message);
            });
            var operations = selected.Select(pair => new PlannedOperation(pair.Value.Text,
                () => executor.ExecuteAsync(pair.Key, shouldCloseFirefox))).ToList();
            var results = await Task.Run(() => OperationRunner.RunAsync(operations, updates));
            int succeeded = results.Count(result => result.Status == OperationStatus.Success);
            int failed = results.Count(result => result.Status == OperationStatus.Failed);
            int skipped = results.Count - succeeded - failed;
            string summary = $"موفق: {succeeded} | ناموفق: {failed} | انجام‌نشده: {skipped}";
            AppendLog(summary);
            // Persist the authoritative result list, independent of queued UI log callbacks.
            try
            {
                string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WindowsOptimizer", "Logs");
                Directory.CreateDirectory(directory);
                logPath = Path.Combine(directory, DateTime.Now.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N") + ".txt");
                File.WriteAllText(logPath, summary + Environment.NewLine + string.Join(Environment.NewLine, details) + Environment.NewLine + string.Join(Environment.NewLine,
                    results.Select(result => $"[{result.Status}] {result.Name}\n{result.Detail}")));
                AppendLog("Log: " + logPath);
            }
            catch (Exception ex) { AppendLog("ثبت فایل گزارش ممکن نشد: " + ex.Message); }
            MessageBox.Show(this, summary + (logPath is null ? "\nثبت فایل گزارش انجام نشد؛ جزئیات داخل پنجره است." : "\nگزارش: " + logPath),
                "نتیجهٔ عملیات", MessageBoxButtons.OK, failed > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            AppendLog("[ERR] " + ex.Message);
            MessageBox.Show(this, "عملیات کامل نشد؛ بخشی از تغییرات ممکن است اعمال شده باشد.\n" + ex.Message,
                "خطا", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            running = false;
            start.Enabled = true;
            foreach (var check in options.Values) check.Enabled = true;
            closeFirefox.Enabled = options[Optimization.Firefox].Checked;
        }
    }
}
