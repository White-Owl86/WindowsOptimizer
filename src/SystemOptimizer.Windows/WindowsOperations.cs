using System.Diagnostics;
using System.Text;
using Microsoft.Win32;
using SystemOptimizer.Core;

namespace SystemOptimizerApp;

internal enum Optimization { Services, Power, Updates, HungApps, Firefox, Tasks, Visuals, Browsers, Pagefile, Temp }

internal sealed class WindowsOperations(Action<string> log)
{
    private readonly CommandRunner commands = new();

    public Task<string> ExecuteAsync(Optimization option, bool closeFirefox) => option switch
    {
        Optimization.Services => DisableServicesAsync(["SysMain", "WSearch", "DiagTrack"]),
        Optimization.Power => PowerAsync(),
        Optimization.Updates => DisableServicesAsync(["wuauserv", "DoSvc"]),
        Optimization.HungApps => CloseHungAppsAsync(),
        Optimization.Firefox => FirefoxAsync(closeFirefox),
        Optimization.Tasks => TasksAsync(),
        Optimization.Visuals => Task.FromResult(Visuals()),
        Optimization.Browsers => Task.FromResult(Browsers()),
        Optimization.Pagefile => PagefileAsync(),
        Optimization.Temp => Task.FromResult(CleanTemp()),
        _ => throw new ArgumentOutOfRangeException(nameof(option))
    };

    private Task<string> PowerShellAsync(string script)
    {
        string wrapped = "$ErrorActionPreference = 'Stop'\n" +
            "[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)\n" +
            "try {\n" + script + "\n} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }";
        string encoded = Convert.ToBase64String(Encoding.Unicode.GetBytes(wrapped));
        string executable = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        return commands.RunAsync(executable, ["-NoLogo", "-NoProfile", "-NonInteractive", "-EncodedCommand", encoded], TimeSpan.FromSeconds(90));
    }

    private async Task<string> DisableServicesAsync(string[] names)
    {
        var errors = new List<string>();
        int changed = 0;
        foreach (string name in names)
        {
            try
            {
                // Names come from the fixed application list, never user input.
                string result = await PowerShellAsync($$"""
                    $service = Get-Service -Name '{{name}}' -ErrorAction SilentlyContinue
                    if ($null -eq $service) { 'NOT_INSTALLED' } else {
                        Set-Service -Name '{{name}}' -StartupType Disabled -ErrorAction Stop
                        if ($service.Status -ne 'Stopped') {
                            Stop-Service -Name '{{name}}' -ErrorAction Stop
                            $service.WaitForStatus('Stopped', [TimeSpan]::FromSeconds(20))
                        }
                        $service.Refresh()
                        if ($service.StartType -ne 'Disabled' -or $service.Status -ne 'Stopped') {
                            throw 'The service did not reach the requested state.'
                        }
                        'DISABLED'
                    }
                    """);
                if (result == "NOT_INSTALLED") { log($"[SKIP] {name}: نصب نیست."); continue; }
                if (result != "DISABLED") throw new InvalidOperationException("Unexpected service verification result.");
                changed++;
                log($"[OK] {name}: توقف سرویس و نوع شروع Disabled تأیید شد.");
            }
            catch (Exception ex) { errors.Add(name + ": " + ex.Message); }
        }
        if (errors.Count > 0) throw new InvalidOperationException("بخشی از تنظیمات ممکن است تغییر کرده باشد.\n" + string.Join("\n", errors));
        if (changed == 0) throw new NotApplicableException("هیچ‌یک از سرویس‌های انتخابی نصب نبودند.");
        return $"وضعیت {changed} سرویس بررسی شد. ویندوز یا سیاست سازمان ممکن است آن را دوباره تغییر دهد.";
    }

    private async Task<string> PowerAsync()
    {
        string powercfg = Path.Combine(Environment.SystemDirectory, "powercfg.exe");
        const string scheme = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        string available = await commands.RunAsync(powercfg, ["/list"], TimeSpan.FromSeconds(30));
        if (!available.Contains(scheme, StringComparison.OrdinalIgnoreCase))
            throw new NotApplicableException("طرح High Performance روی این دستگاه موجود نیست؛ طرح جدیدی ساخته نشد.");
        await commands.RunAsync(powercfg, ["/setactive", scheme], TimeSpan.FromSeconds(30));
        string active = await commands.RunAsync(powercfg, ["/getactivescheme"], TimeSpan.FromSeconds(30));
        if (!active.Contains(scheme, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("فعال‌شدن طرح برق تأیید نشد.");
        return "طرح High Performance فعال شد؛ این کار فرکانس پردازنده را قفل نمی‌کند و می‌تواند مصرف و گرما را افزایش دهد.";
    }

    private static async Task<string> CloseHungAppsAsync()
    {
        int requested = 0;
        using var current = Process.GetCurrentProcess();
        foreach (string name in new[] { "PhotosApp", "Microsoft.Photos", "GameBarPresenceWriter" })
        {
            foreach (var process in Process.GetProcessesByName(name))
            {
                using (process)
                {
                    if (process.SessionId != current.SessionId || process.MainWindowHandle == IntPtr.Zero || process.Responding) continue;
                    if (!process.CloseMainWindow()) throw new InvalidOperationException($"درخواست بستن {name} پذیرفته نشد.");
                    requested++;
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                    try { await process.WaitForExitAsync(timeout.Token); }
                    catch (OperationCanceledException) { throw new InvalidOperationException($"{name} بسته نشد؛ برنامه به‌اجبار خاتمه داده نشد."); }
                }
            }
        }
        if (requested == 0) throw new NotApplicableException("برنامهٔ هدف با پنجرهٔ پاسخ‌نداده پیدا نشد.");
        return $"{requested} پنجره به‌صورت عادی بسته شد.";
    }

    private async Task<string> FirefoxAsync(bool closeFirefox)
    {
        var candidates = new[] {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs")
        }.Where(path => !string.IsNullOrWhiteSpace(path))
         .Select(path => Path.Combine(path, "Mozilla Firefox"))
         .Distinct(StringComparer.OrdinalIgnoreCase)
         .Where(path => File.Exists(Path.Combine(path, "firefox.exe"))).ToList();
        if (candidates.Count == 0) throw new NotApplicableException("نصب معمول Firefox پیدا نشد؛ نصب Store یا سفارشی ممکن است مسیر دیگری داشته باشد.");
        foreach (string directory in candidates)
        {
            var result = FirefoxPolicy.Apply(Path.Combine(directory, "distribution", "policies.json"));
            log(result.Changed ? $"[OK] ترجیح تخلیهٔ تب در کمبود حافظه ثبت شد: {directory}" :
                $"[SKIP] ترجیح قبلی مدیر Firefox حفظ شد: {directory}");
            if (result.BackupPath is not null) log("Backup: " + result.BackupPath);
        }
        if (closeFirefox) await CloseFirefoxAsync();
        return "فایل سیاست بررسی شد. Firefox را با حساب معمول خود دوباره باز کنید و about:policies را بررسی کنید؛ حفظ همهٔ تب‌ها تضمین نمی‌شود.";
    }

    private static async Task CloseFirefoxAsync()
    {
        using var current = Process.GetCurrentProcess();
        foreach (var process in Process.GetProcessesByName("firefox"))
        {
            using (process)
            {
                if (process.SessionId != current.SessionId || process.MainWindowHandle == IntPtr.Zero) continue;
                if (!process.CloseMainWindow()) throw new InvalidOperationException("Firefox درخواست بستن را نپذیرفت؛ تنظیمات ممکن است ثبت شده باشند.");
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
                try { await process.WaitForExitAsync(timeout.Token); }
                catch (OperationCanceledException)
                {
                    throw new InvalidOperationException("Firefox منتظر پاسخ کاربر است یا بسته نمی‌شود؛ به‌اجبار بسته نشد. تنظیمات ثبت‌شده را بعداً بررسی کنید.");
                }
            }
        }
    }

    private async Task<string> TasksAsync()
    {
        string result = await PowerShellAsync("""
            $targets = @(
                '\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser',
                '\Microsoft\Windows\Application Experience\ProgramDataUpdater',
                '\Microsoft\Windows\Customer Experience Improvement Program\Consolidator',
                '\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip',
                '\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector',
                '\Microsoft\Windows\Autochk\Proxy'
            )
            $failures = @()
            $changed = 0
            foreach ($target in $targets) {
                $index = $target.LastIndexOf('\')
                $path = $target.Substring(0, $index + 1)
                $name = $target.Substring($index + 1)
                try {
                    $task = Get-ScheduledTask -TaskPath $path -TaskName $name -ErrorAction SilentlyContinue
                    if ($null -eq $task) { Write-Output "[SKIP] $target : not installed"; continue }
                    $task | Disable-ScheduledTask -ErrorAction Stop | Out-Null
                    $state = Get-ScheduledTask -TaskPath $path -TaskName $name -ErrorAction Stop
                    if ($state.State -ne 'Disabled') { throw 'Disabled state was not verified.' }
                    $changed++
                    Write-Output "[OK] $target : disabled"
                } catch { $failures += "${target}: $($_.Exception.Message)" }
            }
            if ($failures.Count -gt 0) { throw ("Some tasks may have changed. " + ($failures -join '; ')) }
            Write-Output "CHANGED=$changed"
            """);
        if (result.EndsWith("CHANGED=0", StringComparison.Ordinal))
            throw new NotApplicableException(result);
        return result;
    }

    private static void SetDword(RegistryKey hive, string path, string name, int value)
    {
        using var key = hive.CreateSubKey(path, writable: true) ?? throw new IOException("Could not open registry key: " + path);
        key.SetValue(name, value, RegistryValueKind.DWord);
        if (!Equals(key.GetValue(name), value)) throw new IOException("Registry value verification failed: " + name);
    }

    private static string Visuals()
    {
        SetDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "EnableTransparency", 0);
        SetDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects", "VisualFXSetting", 2);
        return "ترجیحات شفافیت و جلوه‌ها برای حساب فعلی ثبت شد؛ اعمال همهٔ جلوه‌ها ممکن است به خروج و ورود یا تنظیم دستی Performance Options نیاز داشته باشد.";
    }

    private static string Browsers()
    {
        if (Environment.OSVersion.Version.Build >= 22000)
            SetDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Dsh", "AllowNewsAndInterests", 0);
        else
            SetDword(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Feeds", "ShellFeedsTaskbarViewMode", 2);
        SetDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "StartupBoostEnabled", 0);
        SetDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "BackgroundModeEnabled", 0);
        SetDword(Registry.LocalMachine, @"SOFTWARE\Policies\Microsoft\Edge", "SleepingTabsEnabled", 1);
        SetDword(Registry.LocalMachine, @"SOFTWARE\Policies\Google\Chrome", "BackgroundModeEnabled", 0);
        SetDword(Registry.LocalMachine, @"SOFTWARE\Policies\Google\Chrome", "HighEfficiencyModeEnabled", 1);
        return "مقادیر سیاست Widgets و مرورگرها ثبت شد؛ اثر آن‌ها به نسخه/ویرایش ویندوز و سیاست سازمان وابسته است. edge://policy و chrome://policy را پس از بازکردن دوباره بررسی کنید.";
    }

    private async Task<string> PagefileAsync()
    {
        await PowerShellAsync("""
            $system = Get-CimInstance -ClassName Win32_ComputerSystem -ErrorAction Stop
            $system | Set-CimInstance -Property @{ AutomaticManagedPagefile = $true } -ErrorAction Stop
            if (-not (Get-CimInstance -ClassName Win32_ComputerSystem -ErrorAction Stop).AutomaticManagedPagefile) {
                throw 'Automatic pagefile management was not enabled.'
            }
            """);
        return "مدیریت خودکار Pagefile توسط ویندوز فعال و بررسی شد؛ راه‌اندازی مجدد ممکن است لازم باشد.";
    }

    private string CleanTemp()
    {
        string temp = Path.GetTempPath();
        var roots = new[] { temp, Path.Combine(Directory.GetParent(Environment.SystemDirectory)!.FullName, "Temp") }
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var protectedPaths = new[] {
            AppContext.BaseDirectory,
            Path.GetDirectoryName(Environment.ProcessPath!)!,
            Path.Combine(temp, ".net")
        };
        long bytes = 0;
        int failures = 0;
        foreach (string root in roots)
        {
            var result = TempCleaner.Clean(root, DateTime.UtcNow.AddDays(-7), protectedPaths);
            log($"{root}: deleted={result.Deleted}, skipped={result.Skipped}, failed={result.Failed}");
            bytes += result.BytesDeleted;
            failures += result.Failed;
        }
        if (failures > 0) throw new IOException($"پاکسازی جزئی انجام شد؛ {failures} خطای دسترسی. فضای آزادشده: {bytes / 1048576.0:0.0} MB");
        return $"فایل‌های موقت قدیمی پاک شدند؛ فایل‌های جدید، قفل‌شده و مسیرهای پیوندی کنار گذاشته شدند. فضای آزادشده: {bytes / 1048576.0:0.0} MB";
    }
}
