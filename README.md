# Windows Optimizer 5

[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%2F%2011%20x64-blue.svg)](https://microsoft.com/windows)
[![Framework](https://img.shields.io/badge/.NET-10.0-purple.svg)](https://dotnet.microsoft.com)
[![Language](https://img.shields.io/badge/Language-C%23-brightgreen.svg)](https://learn.microsoft.com/dotnet/csharp/)

[English](#english) | [فارسی](#فارسی)

---

## English

A modern, refactored C# (.NET 10) Windows 10 and 11 (x64) optimization and tuning application designed for safety, transparency, and DPI-aware modern user experience.

### Download & Running

#### 1. Pre-built Executable
Download the packaged release from the [Releases](https://github.com/White-Owl86/WindowsOptimizer/releases) section:
- Extract `SystemOptimizer-win.zip` and run `SystemOptimizer.exe`.
- Requires Administrator privileges (UAC prompt will appear).
- No manual option is selected by default; review warnings before proceeding with optimizations.

#### 2. Running from Source
Requires **.NET SDK 10.0.401** (configured in `global.json`):
```powershell
# Run regression tests
dotnet run --project tests/SystemOptimizer.Tests -c Release

# Build Windows Forms application
dotnet build src/SystemOptimizer.Windows -c Release
```

### Key Safety Improvements & Architecture

- **Safe Process Execution:** Replaces arbitrary `cmd /c` shell calls with direct process argument parsing, execution timeouts, and comprehensive stderr/exit code capture.
- **Accurate State Tracking:** Tracks every operation status (Success, Failed, Skipped). Verifies system state (services, power plans, scheduled tasks, Pagefile) after changes.
- **Firefox Policy Safety:** Safely merges browser policies with existing preferences without destroying custom settings. Backs up original policy files (`.bak`).
- **Careful Temp Cleanup:** Restricts file removal to temp files older than 7 days. Strictly avoids symbolic links, junctions, active runtime paths, and Windows Update cache.
- **Browser & Power Tuning:** Supports Edge Sleeping Tabs and Chromium High Efficiency mode policies, alongside verifiable High Performance power plans.
- **Audit Logging:** Detailed step logs and operational history are automatically written to `%LOCALAPPDATA%\WindowsOptimizer\Logs`.

---

## فارسی

نسخهٔ اصلاح‌شدهٔ برنامهٔ C# ارسال‌شده، با رابط فارسی برای ویندوز ۱۰ و ۱۱ نسخهٔ **x64**.

### دانلود و اجرا

فایل اجرایی برنامه از بخش [Releases](https://github.com/White-Owl86/WindowsOptimizer/releases) قابل دانلود است. فایل را دانلود و اجرا کنید؛ ویندوز درخواست دسترسی Administrator نشان می‌دهد. برنامه امضای دیجیتال ندارد.

هیچ گزینه‌ای در شروع انتخاب نشده است. گزینه‌ها را انتخاب کنید، توضیحات هشدار را بخوانید و در صورت تمایل تأیید کنید. پیش‌فرض تأیید **خیر** است. هنگام اجرای عملیات، تغییر گزینه‌ها و بستن پنجره مسدود می‌شود تا نتیجه ثبت شود.

تنظیمات CurrentUser و محل گزارش مربوط به **حسابی است که برنامه با آن اجرا می‌شود**؛ نام آن در پنجره نمایش داده می‌شود. اگر برای UAC حساب دیگری وارد کنید، تنظیمات کاربری روی همان حساب مدیر اعمال می‌شوند.

### ایرادهای اصلاح‌شده

- فرمان‌ها دیگر از طریق `cmd /c` اجرا نمی‌شوند؛ آرگومان‌ها جدا ارسال شده و کد خروج، خروجی خطا و مهلت اجرا بررسی می‌شوند. فرمانِ زمان‌تمام‌شده خاتمه داده می‌شود و احتمال تغییر جزئی گزارش می‌شود.
- نتیجهٔ هر گزینه به‌صورت موفق، ناموفق یا انجام‌نشده ثبت می‌شود. موفقیت کلی بدون توجه به خطاها اعلام نمی‌شود. سرویس‌ها، طرح برق، تسک‌ها و Pagefile پس از تغییر بررسی می‌شوند.
- سیاست Firefox با تنظیمات موجود ادغام می‌شود؛ JSON خراب بازنویسی نمی‌شود. از فایل قبلی نسخهٔ `.bak` کنار آن ذخیره می‌شود و ترجیح موجود مدیر حفظ می‌شود. غیرفعال‌کردن آپدیت Firefox، محدودیت تعداد پردازه‌ها و کاهش دفعات ذخیرهٔ نشست حذف شده‌اند.
- Firefox به‌اجبار کشته یا با دسترسی مدیر دوباره اجرا نمی‌شود. گزینهٔ بستن فقط درخواست عادی می‌فرستد؛ مرورگر را خودتان با حساب معمول باز کنید. حفظ همهٔ تب‌ها تضمین نمی‌شود.
- Photos و GameBar فقط در صورت پاسخ‌ندادن پنجره در نشست فعلی، درخواست بستن دریافت می‌کنند؛ سرویس‌های Xbox در این گزینه تغییر نمی‌کنند.
- پاکسازی به فایل‌های Temp با آخرین تغییر بیش از ۷ روز محدود است. کش دانلود Windows Update و Crash Dump حذف نمی‌شوند. پیوندها، junctionها، فایل‌های جدید و مسیر اجرای برنامه کنار گذاشته می‌شوند؛ پوشه‌ها حذف نمی‌شوند.
- نام Celeron/Pentium دیگر مبنای قفل‌کردن گزینهٔ برق نیست. High Performance انتخابی است، تنها در صورت موجودبودن طرح فعال می‌شود و ادعای قفل‌کردن فرکانس CPU وجود ندارد.
- گزینهٔ Pagefile واقعاً مدیریت خودکار حافظهٔ مجازی را فعال می‌کند؛ تنظیمات پاک‌کردن Pagefile هنگام خاموش‌شدن و LargeSystemCache تغییر نمی‌کنند.
- سیاست Sleeping Tabs در Edge افزوده شده و سیاست Widgets ویندوز ۱۱ از مسیر مستند آن ثبت می‌شود. ثبت مقدار رجیستری به معنی تأیید اثر آن در همهٔ ویرایش‌های ویندوز نیست؛ مرورگرها را در `edge://policy` و `chrome://policy` بررسی کنید.
- رابط قابل تغییر اندازه و سازگار با DPI است؛ خروجی مراحل در فایل گزارش نیز ذخیره می‌شود.

### اثر تغییرات و بازیابی

خاموش‌کردن Windows Search می‌تواند جست‌وجو را کندتر کند؛ خاموش‌کردن Update دریافت خودکار به‌روزرسانی‌های امنیتی را مختل می‌کند. ویندوز یا سیاست سازمان ممکن است وضعیت سرویس‌ها را دوباره تغییر دهد. افزایش سرعت یا کاهش مصرف رم برای همهٔ دستگاه‌ها تضمین نمی‌شود.

بازگردانی خودکار همهٔ عملیات وجود ندارد. شکست یک مرحله، تغییرات مراحل قبلی را برنمی‌گرداند. تنظیمات سرویس، طرح برق و Pagefile را از ابزارهای ویندوز مدیریت کنید. برای بازیابی سیاست Firefox، پس از بررسی تغییرات جدید، نسخهٔ `.bak` گزارش‌شده را به جای `policies.json` قرار دهید. حذف فایل موقت قابل برگشت از داخل این برنامه نیست.

گزارش‌ها در `%LOCALAPPDATA%\WindowsOptimizer\Logs` ذخیره می‌شوند. قابلیت‌های این برنامه شامل غیرفعال‌کردن Defender یا فایروال نیست.

### ساخت از سورس

SDK تعیین‌شده در `global.json`، یعنی **.NET SDK 10.0.401** را نصب کنید. از داخل این پوشه:

```powershell
dotnet run --project tests/SystemOptimizer.Tests -c Release
dotnet build src/SystemOptimizer.Windows -c Release
```

پروژه به بستهٔ جانبی NuGet وابسته نیست.

### منابع

- [انتشار single-file و runtime مستقل — Microsoft](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview)
- [سیاست Preferences در Firefox — Mozilla](https://mozilla.github.io/policy-templates/#preferences)
- [SleepingTabsEnabled — Microsoft Edge](https://learn.microsoft.com/en-us/deployedge/microsoft-edge-browser-policies/sleepingtabsenabled)
- [AllowNewsAndInterests — Microsoft](https://learn.microsoft.com/en-us/windows/client-management/mdm/policy-csp-newsandinterests)
- [HighEfficiencyModeEnabled — Chromium](https://github.com/chromium/chromium/blob/main/components/policy/resources/templates/policy_definitions/Miscellaneous/HighEfficiencyModeEnabled.yaml)
