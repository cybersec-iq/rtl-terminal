# وضعیت RTL Terminal برای Workspace CLI Agentic

تاریخ: ۱۴۰۵/۰۷/۲۶ (2026-10-18)

## وضعیت فعلی

`USER_INSTALL_RUNNING_GITHUB_RELEASE_PUBLISHED_CI_PASS`

نسخهٔ `1.1.4` در مسیر نصب کاربری اجرا شده و پنجره پاسخ‌گو است (PID `8748`). نصب سیستمی `1.1.2` همچنان دست‌نخورده و باز است (PID `34028`)؛ این محیط مجوز نوشتن در `Program Files` نداد.

## مرجع و نسخه‌ها

- مخزن منبع: `C:\Users\arian\Desktop\my project\rtl-terminal`
- کنترل‌پک اختصاصی برای پروژه در مسیرهای مرجع بررسی‌شده پیدا نشد؛ `README.md` و کد مخزن مبنا هستند.
- `main` از commit `8fda31b` شروع شد که با آخرین `origin/main` و Release بررسی‌شده (`1.1.2`) برابر بود.
- نصب سیستمی قبلی: `C:\Program Files\AryanIQ\Rtl Terminal\RtlTerminal.exe` نسخهٔ `1.1.2`؛ برای برگشت حفظ شد.
- نسخهٔ جدید: `C:\Users\arian\AppData\Local\Programs\AryanIQ\Rtl Terminal 1.1.4\RtlTerminal.exe`.
- پشتیبان کامل نسخهٔ قبلی: `C:\Users\arian\AppData\Local\AryanIQ\Rtl Terminal Backups\1.1.2-before-1.1.4`; هش exe و شمار فایل‌ها با اصل برابر بود.
- میانبر Start Menu مخصوص کاربر به exe جدید و آیکون تعبیه‌شدهٔ آن اشاره می‌کند؛ میانبر و نصب سیستم قبلی تغییر نکردند.
- سنجاق Taskbar کاربر به exe جدید تغییر کرد؛ فایل میانبر سنجاق‌شدهٔ قبلی در پوشهٔ پشتیبان حفظ شد.

## ممیزی و تغییرات

- بازبینی کد برای میان‌برها، Select all، رفتار دنبال‌کردن خروجی، جهت RTL و طرح‌های رنگ موجود انجام شد؛ این بازبینی استاتیک است و تأیید اجرای همهٔ CLIها محسوب نمی‌شود.
- دکمهٔ Voice فقط در نشست Codex فعال می‌شود و `/voice` یا `/voice stop` را به همان نشست می‌فرستد. پیاده‌سازی تشخیص گفتار SAPI ویندوز حذف شده است؛ صدا را خود Codex CLI دریافت می‌کند.
- دکمهٔ `Copy report` کلید میان‌بر بومی Codex (`Ctrl+O`) را می‌فرستد تا آخرین پاسخ کامل‌شده در قالب Markdown کپی شود.
- `Ctrl+A` متن نگه‌داشته‌شده را انتخاب می‌کند و با `Escape` یا زدن دوبارهٔ `Ctrl+A` انتخاب سراسری پاک می‌شود.
- دکمهٔ `Select` کنار `+` پوشه‌گزین و سپس نام پروژه/تب را می‌گیرد. تب جدید در همان مسیر و با پروفایل تب فعال ساخته می‌شود؛ اگر Codex CLI یا Claude Code در تب فعال شناسایی شود، همان CLI هم خودکار پس از آماده‌شدن shell راه می‌افتد.
- راست‌کلیک تب گزینهٔ `Rename tab...` دارد؛ نام دلخواه تنها برای همان تب باز اعمال می‌شود و با بستن تب از بین می‌رود.
- میان‌بر `Ctrl+Alt+V` به CLI تعاملی کلید Paste خام می‌فرستد تا تصویر کلیپ‌بورد به برنامه‌ای که از paste تصویر پشتیبانی می‌کند برسد.
- رنگ Phosphor و طرح‌های آمادهٔ موجود حفظ شده‌اند.

## بیلد و اعتبارسنجی

- نسخهٔ سورس: `1.1.4`.
- بیلد Release موفق شد: `publish\win-x64\RtlTerminal.exe` نسخهٔ `1.1.4.0`، self-contained برای `win-x64`.
- SHA-256: `63A569B0CDBB62CA4B39150F1E116DBB82A53E7B5DB8C20C21E662DFD79DE0A9`.
- بیلد Release موفق شد: `publish\win-x64\RtlTerminal.exe` نسخهٔ `1.1.4.0`، self-contained برای `win-x64`.
- SHA-256: `1734A96EA36565E8E2198DFFA99B82D94349BC65544DBF95701401F8B4EC3237`.
- Runtime check فقط وجود پنجرهٔ جدید و پاسخ‌گویی آن را تأیید کرد؛ آزمون UI برای Voice، انتخاب پوشه، اجرای خودکار Agent و paste تصویر انجام نشده است.
- commit محصول `bea30f9` و تگ `v1.1.4` به `origin` ارسال شدند؛ GitHub Release ساخته شد و `RtlTerminal-Setup-1.1.4-x64.exe` بارگذاری شد.
- workflow انتشار موفق شد و [GitHub Release v1.1.4](https://github.com/cybersec-iq/rtl-terminal/releases/tag/v1.1.4) با فایل Setup ساخته شد.
- workflow اعتبارسنجی `main` ابتدا به‌علت assertion قدیمی تعداد گزینه‌های منوی راست‌کلیک شکست خورد؛ آزمون به تعداد ۹ گزینهٔ منو همگام شد. اجرای بعدی build، Buffer tests و Render/theme tests را با موفقیت گذراند: [run 39159465151](https://github.com/cybersec-iq/rtl-terminal/actions/runs/39159465151).
- SHA-256 فایل Setup گیت‌هاب: `a33a7805ef463a9984032c3fc0dfb2ebaeff173aaf8ffd8341ec1fa027d2f7ea`.

## قدم بعد

پس از بررسی کاربر، اگر نصب سیستمی `1.1.2` باید حذف شود، مسیر و میانبر قدیمی را جداگانه پاک کن؛ نسخهٔ `1.1.4` در نصب کاربری نگه داشته شود.

## فهرست درخواست‌های قابلیت

درخواست‌های قابلیت کاربر، دسته‌بندی‌شده و با وضعیت سورس و تأیید اجرایی جداگانه، در `feature/FEATURE_BACKLOG.md` ثبت می‌شوند.
