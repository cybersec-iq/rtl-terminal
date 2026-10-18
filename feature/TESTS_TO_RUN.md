# آزمون‌های نهایی قابلیت‌ها

تاریخ اجرا: 2026-10-18. اجرا روی سورس محلی RTL Terminal در مسیر `C:\Users\arian\Desktop\my project\rtl-terminal`؛ نسخه‌های نصب‌شده تغییر نکردند. `PASS` یعنی همان دامنهٔ مشخص‌شده آزمون خودکار را گذرانده؛ آزمون خودکار، تأیید کامل جریان رابط یا Provider زنده نیست.

## F-001 — تغییر نام تب

| ID | آزمون | روش و انتظار | وضعیت | نتیجه/تاریخ |
|---|---|---|---|---|
| T-001 | تغییر نام تب عادی | راست‌کلیک تب، Rename، نام تازه؛ فقط همان تب عنوان تازه بگیرد. | PENDING_MANUAL | آزمون رابط در پنجرهٔ preview انجام نشده؛ 2026-10-18 |

## F-002 — پروژه‌ها، سشن‌ها و X Agents CLI

| ID | آزمون | روش و انتظار | وضعیت | نتیجه/تاریخ |
|---|---|---|---|---|
| T-002 | منوی انتخاب Shell و موتور | `+` → CMD/PowerShell/WSL → X Agents CLI؛ موتور در سشن پروژه اجرا شود. | PENDING_MANUAL | منطق/بیلد بررسی شد؛ بازبینی جریان UI و اجرای Agent واقعی انجام نشده؛ 2026-10-18 |
| T-003 | ساخت پروژه و سشن اول | نام پروژه، چند مسیر و نام سشن؛ رکوردها در SQLite بمانند و تب عنوان مناسب بگیرد. | PARTIAL | SQLite و ایجاد تراکنشی/بازیابی با آزمون خودکار PASS؛ پنجرهٔ ساخت و File Explorer دستی بررسی نشده؛ 2026-10-18 |
| T-004 | انتخاب سشن موجود | پروژه را از منوی `+` انتخاب کن؛ سشن موجود در همان تب پروژه فعال شود. | PARTIAL | از منوی همان پروژه بین دو سشن جابه‌جا شد و شناسهٔ فعال در دیتابیس آزمایشی عوض شد؛ شمار تب‌ها و ادامهٔ دو CLI زنده جداگانه سنجیده نشد؛ 2026-10-18 |
| T-005 | ساخت سشن تازه | از `Sessions` گزینهٔ `+`؛ نام و مسیرهای سشن دریافت و ذخیره شوند. | PASS_UI_PARTIAL | در preview Release با UI سشن `Manual Test Session` ساخته شد، گزینهٔ No برای مسیر افزوده انتخاب شد و رکورد/سشن فعال در دیتابیس موقت تأیید شد؛ Add Folder مسیر واقعی و سشن CLI تعاملی هنوز جداگانه سنجیده نشده؛ 2026-10-18 |
| T-006 | جابه‌جایی و عدم اختلاط | بین دو سشن جابه‌جا شو؛ خروجی، عنوان و مسیر جدا بمانند و تب پروژه یکی بماند. | PASS_UI_PARTIAL | از زیرمنوی یک پروژه به هر دو سشن سوییچ شد و مقدار `ActiveSessionId` پس از هر سوییچ تغییر کرد؛ بازگشت runtimeهای زنده و تعداد تب‌ها/متن هر دو CLI هنوز کامل سنجیده نشده؛ 2026-10-18 |
| T-007 | ویرایش پروژه و سشن | نام‌ها و مسیرهای پروژه/سشن را ویرایش کن؛ بعد از اجرا دوباره حفظ شوند. | PASS_NAME_UI_PARTIAL_PATHS | نام پروژه و نام سشن در UI ویرایش و در SQLite تأیید شد؛ مسیرها در آزمون خودکار پوشش دارند، ولی ذخیرهٔ تغییر مسیر از UI و راه‌اندازی دوبارهٔ آن هنوز دستی کامل نشده؛ 2026-10-18 |
| T-008 | ماندگاری بستن تب | خروجی بنویس، تب را ببند و دوباره باز کن؛ transcript بازیابی شود. | PARTIAL | SQLite transcript و checkpoint خودکار پوشش دارند؛ بستن/بازکردن تب از رابط دستی اجرا نشده؛ 2026-10-18 |
| T-009 | ماندگاری اجرای دوبارهٔ برنامه | پروژه و سشن فعال بساز، برنامه را ببند و دوباره اجرا کن؛ تب/سشن و داده‌ها باقی بمانند. | PASS_UI_PARTIAL | preview Release را با دیتابیس موقت بستیم و دوباره اجرا کردیم؛ همان پروژه، نام سشن فعال و عنوان تب `Cascade Preview — Renamed UI Session` پس از راه‌اندازی برگشتند؛ نصب اصلی جدا ماند؛ 2026-10-18 |
| T-010 | مسیرهای چندگانه در Agent | مسیر کاری اول و مسیرهای اضافهٔ پشتیبانی‌شده به Provider برسند؛ محدودیت‌ها اعلام شوند. | PARTIAL | `node --test tests/AgentEngine/agent-engine.test.cjs`: دو آزمون PASS برای اعتبارسنجی ریشه‌ها و working directory سشن؛ اجرای واقعی Provider/تمام adapterها آزموده نشده؛ 2026-10-18 |
| T-011 | سوییچ Provider در همان سشن پروژه | بین Codex و Claude جابه‌جا شو؛ هر دو باید همان شناسهٔ سشن پروژه و مسیر کاری را داشته باشند، و هر Provider در بازگشت به گفت‌وگوی بومی خودش ادامه دهد. | PARTIAL | آزمون Node جدید PASS: هر دو launch به همان `projectSessionId` و `cwd` وصل شدند و هر کدام native conversation خودش را resume کرد؛ سوییچ از UI و Provider زنده هنوز دستی بررسی نشده؛ 2026-10-18 |
| T-012 | اعلان پایان Agent | اعلان همان سشن، صدای کوتاه و شمارندهٔ ماندگار ثبت شوند. | PARTIAL | ذخیره‌سازی/حذف‌تکرار و wildcard monitor با آزمون C# PASS؛ اعلان و صدا در UI با Agent زنده دستی بررسی نشده؛ 2026-10-18 |
| T-013 | جداسازی runtime سشن‌ها | runtime هر سشن مستقل باشد و فایل‌های upstream دست‌نخورده بمانند. | PARTIAL | آزمون Node PASS برای مسیرهای runtime هر سشن؛ hash برابری نسخه‌های کپی route-ui/route-hub/generic-broker با مبدأ تأیید شد؛ چرخهٔ کامل چند سشن از UI اجرا نشده؛ 2026-10-18 |
| T-014 | سه Shell | جریان پروژه را جداگانه در CMD، PowerShell و WSL اجرا کن. | PARTIAL | CMD و PowerShell برای round-trip مسیرهای خاص واقعی اجرا شدند؛ پروب واقعی WSL در Ubuntu شامل `wslpath`، دسترسی فایل و متغیرهای محیطی PASS شد. مسیر ساخت/انتخاب پروژه تا اجرای Shell از UI هنوز بررسی نشده؛ 2026-10-18 |
| T-015 | لغو ساخت پروژه/سشن | در پنجرهٔ ساخت Cancel کن؛ رکورد نیمه‌کاره نماند. | PARTIAL | ایجاد پروژه و سشن اول تراکنشی است و مسیر Cancel در منطق ذخیره‌سازی/ایجاد اعمال شده؛ UI Cancel دستی بررسی نشده؛ 2026-10-18 |
| T-016 | اتصال مجدد به Shell پایدار | سشن پروژه را باز کن، خروجی marker بگیر، کلاینت UI را detach کن، دوباره با همان شناسه وصل شو و marker را بخوان؛ سپس broker را تمیز متوقف کن. | PASS | اجرای جداگانهٔ `--persistent-pty-smoke`: شناسهٔ نامعتبر رد شد و marker پس از detach/reconnect برگشت. یک اجرای هم‌زمان با تست‌های دیگر timeout داد؛ اجرای مجزای دوباره PASS شد؛ 2026-10-18 |
| T-017 | تداوم Provider و handoff | شناسهٔ native برای adapterهای ثبت‌شده به همان پروژه/سشن وصل بماند؛ CLI بدون adapter صریحاً handoff-only باشد و متن transcript در دسترس بماند. | PASS | Node suite شش‌تایی: native state، Claude create/resume، handoff و handoff-only همه PASS؛ 2026-10-18 |

## اجراهای خودکار انجام‌شده

- `dotnet build RtlTerminal.sln -c Release --no-restore --nologo` — PASS، صفر هشدار و خطا پس از اتصال broker.
- `dotnet run --no-build -c Release --project tests\RtlTerminal.BufferTests\RtlTerminal.BufferTests.csproj` — PASS، ۱۶/۱۶؛ شامل اجرای واقعی CMD و Windows PowerShell برای round-trip مسیرهای دارای نویسه‌های خاص.
- `dotnet run --no-build -c Release --project tests\RtlTerminal.RenderTests\RtlTerminal.RenderTests.csproj` — PASS در اجرای مجدد؛ suite کامل شامل SQLite، monitor، رندر/RTL، تم، layout، دو Shell زنده، drag/drop و ConPTY. اجرای هم‌زمان اولیه یک بار در تست دو Shell timeout داد و اجرای مجدد کامل پاس شد.
- همان RenderTests با `--persistent-pty-smoke` — PASS، رد شناسهٔ بد و detach/reconnect واقعی marker با توقف تمیز broker.
- همان RenderTests با `--window-smoke` — PASS، منوی زمینه، maximize/restore و محدودهٔ پنجره.
- همان RenderTests با `--conpty-persian` — PASS، ConPTY واقعی و رندر Persian/ZWNJ با خاموشی تمیز.
- همان RenderTests با `--wsl-path-probe Ubuntu` — PASS، `wslpath` واقعی، مسیر دارای فاصله/نویسهٔ خاص، دسترسی فایل، share هم‌توزیع و انتقال متغیرهای محیطی.
- `node --test tests\AgentEngine\agent-engine.test.cjs` — PASS، ۷/۷؛ مورد تازهٔ Codex↔Claude ثابت کرد هویت سشن پروژه و مسیر کاری مشترک می‌ماند و هر Provider گفت‌وگوی native خودش را resume می‌کند. `node --check` برای چهار فایل AgentEngine — PASS.
- `dotnet publish RtlTerminal.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:PublishDir=publish\feature-preview-1.1.4-project-sessions\ --nologo` — PASS؛ preview تازه ساخته شد.

## محدودیت دامنهٔ اعتبارسنجی

آزمون خودکار و UI Automation، پذیرش کامل کاربر را ثابت نمی‌کنند. T-001 تا T-009 و بخش UI در T-010 تا T-015 هنوز PASS دستی ندارند؛ به‌ویژه ساخت/ویرایش کامل پروژه و سشن، سوییچ زنده بین دو سشن، حفظ داده پس از بستن/اجرای دوبارهٔ خود برنامه و اعلان/صدای Agent. T-018 در preview ایزولهٔ Release با حرکت واقعی نشانگر ماوس روی Shell، Agent، Project، Session و Folders بررسی شد؛ زنجیرهٔ منوها کنار هم باز ماند و زیرمنوی مدیریت پروژه هم باز شد. تصویر آخرین اجرای Release در `publish/project-cascade-release-hover.png` ثبت شده است. ظاهر منو با تم تیرهٔ ترمینال هماهنگ بود؛ این بررسی، پذیرش نهایی کاربر را جایگزین نمی‌کند. تست‌های Shell جداگانه CMD، PowerShell و WSL را اجرا کردند، ولی ورود از منوی پروژه تا Shell/Agent واقعی تا انتها بررسی نشده است. اجرای Provider زنده/اعتبار حساب انجام نشده است. هر Provider با سشن native مستقل خودش ادامه می‌دهد؛ انتقال عینِ تاریخچهٔ گفت‌وگو بین Providerها شرط F-002 نیست. نصب‌های قبلی تغییر نکرده‌اند.

## تکمیل منوهای آبشاری و حذف پروژه/سشن (2026-10-18)

| ID | آزمون | روش و انتظار | وضعیت | نتیجه |
|---|---|---|---|---|
| T-018 | ساختار منوی آبشاری | ساختار Shell → X Agents CLI → Project → Session و زیرمنوهای مدیریت پروژه/سشن با گزینه‌های لازم بررسی شود. | PASS_AUTO + PASS_UIA + PASS_MOUSE_HOVER | آزمون WPF ساختار را سنجید؛ در preview جدا، ماوس واقعاً روی Shell، Agent، Project، Session و Folders رفت و زیرمنوها کنار هم باز شدند. `Manage project` نیز Rename/Folders/Delete را در زیرمنوی کناری نشان داد. تصویر آخرین اجرای Release: `publish/project-cascade-release-hover.png`. هیچ عملیات حذف تأیید نشد. |
| T-019 | حذف سشن/پروژه | حذف سشن فعال، انتخاب سشن جایگزین و پاک‌شدن runtime همان سشن؛ حذف پروژه، پاک‌شدن رکوردهای وابسته؛ پوشهٔ واقعی بماند. | PASS_AUTO | آزمون SQLite/runtime تأیید کرد رکوردها و runtime سشن حذف شدند، سشن جایگزین فعال شد، حذف پروژه cascade شد و فایل پوشهٔ واقعی باقی ماند. |
| T-020 | فرم‌های هماهنگ با پوسته | پنجره‌های نام‌گذاری، ویرایش مسیر و تأیید حذف با تم ترمینال نمایش و تعامل کنند. | PASS_UIA_PARTIAL_VISUAL | در اجرای فعلی Release پنجرهٔ Rename project با برچسب Project / tab name، دکمه‌های Save/Cancel باز شد و با Cancel بسته شد؛ داده‌ای تغییر نکرد. فرم ویرایش پوشه و تأیید حذف نیز در preview ایزولهٔ قبلی با Cancel بررسی شدند. پوستهٔ منو در تصویر Release بازبینی شد؛ پذیرش بصری نهایی کاربر هنوز لازم است. |

## اجرای آزمون‌های این تکمیل

- `dotnet build tests/RtlTerminal.RenderTests/RtlTerminal.RenderTests.csproj -c UITest --no-restore --nologo` — PASS، صفر هشدار و خطا.
- `dotnet run --project tests/RtlTerminal.RenderTests/RtlTerminal.RenderTests.csproj -c UITest --no-build` — PASS، کل suite؛ شامل T-018 و T-019.

منوی آبشاری در preview Release جدا با هاور فیزیکی و UI Automation تعامل شد؛ `dotnet publish` چندفایلی Release موفق بود و برنامه با دیتابیس موقت جدا اجرا شد. پنجرهٔ Rename نیز باز و با Cancel بسته شد. عملیات Delete عمداً تأیید نشد؛ حذف داده فقط در آزمون خودکار SQLite پوشش دارد. پذیرش نهایی بصری توسط کاربر هنوز لازم است.

## F-003 — حساب‌های Coding Agents

| ID | آزمون | روش و انتظار | وضعیت | نتیجه/تاریخ |
|---|---|---|---|---|
| T-021 | فهرست پنج حساب Codex | فقط Account 1–5 از `.codex-account1` تا `.codex-account5`؛ بدون فرمان ورود؛ انتخاب، همان `CODEX_HOME` و تنظیم file-auth را اجرا کند. | PASS_AUTO | آزمون واحد پوشه‌ها و launch spec PASS؛ آزمون host catalog وجود هر پنج پروفایل، از جمله پنجم، را تأیید کرد؛ 2026-10-18 |
| T-022 | ایمیل، طرح و Usage زندهٔ Codex | App Server همان پنج home را بخواند؛ پاسخ‌ها به حساب درست متصل باشند و هیچ درخواست مدل نفرستند. | PASS_LIVE_READ | خواندن read-only هر پنج home موفق شد و هرکدام email و rate limits برگرداند؛ خروجی حساس ایمیل ذخیره/گزارش نشد؛ مقدار Usage فقط در همان منوی حساب نمایش داده می‌شود؛ 2026-10-18 |
| T-023 | انتخاب حساب Codex در رابط | بازکردن Account 1–5 در تب‌های جدا؛ هرکدام همان ورود قبلی را ادامه دهد و هیچ صفحهٔ ورود/خروج حساب نشان ندهد. | PENDING_MANUAL | ساختار/فرمان با آزمون خودکار تأیید شد؛ کلیک و اجرای تعاملی Codex از UI انجام نشده؛ 2026-10-18 |
| T-024 | منوی Antigravity | Coding Agents → Antigravity، ورود نخست با خود `agy`، ورود بعدی همان دادهٔ Windows Credential Manager و Usage رسمی؛ Gemini CLI استفاده نشود. | PASS_LIVE_READ + PASS_PREVIEW + PENDING_MANUAL | Windows Credential Manager هدف Antigravity داشت و `agy -p /usage --output-format json` با exit 0 دو گروه Usage و `total_tokens=0` برگرداند. Preview حساب را نشان داد و ۴ ردیف Usage رندر کرد؛ ورود جدید و کلیک تعاملی انجام نشد. ایمیل در header خود agy نمایش داده می‌شود، اما خروجی machine-readable مصرف آن را برنمی‌گرداند؛ 2026-10-18 |
| T-025 | منوی Claude Code و افزودن حساب | حساب موجود حفظ شود؛ Add account از `CLAUDE_CONFIG_DIR` جداگانه و `claude auth login` استفاده کند. | PENDING_MANUAL | مسیر و منطق سورس اضافه شد؛ ورود تعاملی انجام نشده. منبع وضعیت فعلی ایمیل/Usage در این پیاده‌سازی در دسترس نیست؛ 2026-10-18 |
| T-026 | منو و ترتیب Providerها | Coding Agents ترتیب Codex → Claude Code → Antigravity؛ ورودی قدیمی Antigravity Sessions دیگر در منوی اصلی/Free Agent دیده نشود و دادهٔ ذخیره‌شده دست‌نخورده بماند. | PASS_AUTO + PASS_PREVIEW + PENDING_UI | آزمون تابع ترتیب پاس شد؛ preview منوهای Main، Coding Agents و Free Agent را نشان داد و ورودی Antigravity Sessions در دو منوی اول/Free نبود؛ تأیید بصری UI نهایی باقی است؛ هیچ رکوردی پاک نشد؛ 2026-10-18 |
| T-027 | جداسازی متغیرهای دسترسی | اجرای پروفایل با حذف متغیرهای API موروثی؛ متغیر `null` نباید به مقدار متنی `"null"` تبدیل شود. | PASS_AUTO | آزمون merge محیطی کلیدهای OpenAI را case-insensitive حذف کرد و مسیر منتخب را حفظ کرد؛ 2026-10-18 |

### اجرای آزمون‌های F-003

- `node --test tests/AgentEngine/codex-account-status.test.cjs tests/AgentEngine/agent-engine.test.cjs` — 14/14 PASS؛ `node --check` برای `xagent.js` و `codex-account-status.js` نیز PASS؛ 2026-10-18.
- App Server واقعی: پنج home پاسخ هویت و Usage دادند؛ مدل فراخوانی نشد. ایمیل‌ها عمداً در گزارش/فایل درج نشدند.
- preview واقعی: Main، Coding Agents، Free Agent و Antigravity رندر شدند؛ ترتیب Codex → Claude Code → Antigravity مشاهده شد؛ منوی Antigravity چهار ردیف Usage نشان داد و ورودی قدیمی Antigravity Sessions در Main و Free Agent نبود.
- `dotnet build RtlTerminal.csproj -c Release --no-restore --nologo -o "$env:TEMP\\rtl-terminal-coding-accounts-build"` — PASS، صفر هشدار/خطا. بیلد معمولی به‌دلیل قفل exe توسط RTL Terminal باز شکست خورد؛ هیچ پردازشی بسته نشد و خروجی ایزوله در Temp ساخته شد.
- Antigravity و Claude: آزمون رابط/ورود تعاملی باقی است؛ هیچ ورود یا logout واقعی انجام نشده است.

### اعتبارسنجی پیش از انتشار 1.1.5

- بیلد Release برنامه در مسیر موقت با `dotnet build RtlTerminal.csproj -c Release --no-restore --nologo -o "$env:TEMP\rtl-terminal-1.1.5-validation\app"` موفق شد؛ صفر هشدار/خطا.
- آزمون‌های Buffer موفق شدند. ساخت RenderTests در مسیر موقت موفق شد، اما اجرای کامل آن در `CheckLivePaneSessions` با `Live pane output timed out` متوقف شد؛ ترمینال‌های در حال استفاده باز نگه داشته شدند.
- `dotnet build RtlTerminal.sln` به قفل بودن DLL/EXE توسط پنجرهٔ باز `RtlTerminal (20284)` برخورد کرد؛ هیچ پردازشی بسته نشد. اجرای تمیز Windows CI پس از push معیار نهایی این بخش است.
- Node suite ایزوله: 14/14 PASS؛ `node --check` برای فایل‌های موتور PASS.

### انتشار و نصب محلی 1.1.5

- GitHub `main`: commit `b833df92339560c923eb9677ce3897ceed3dd1a4`؛ Windows CI run `39172449464` با Build، Buffer و Render همگی موفق.
- Release عمومی: [v1.1.5](https://github.com/cybersec-iq/rtl-terminal/releases/tag/v1.1.5)؛ workflow `39172564552` موفق؛ asset `RtlTerminal-Setup-1.1.5-x64.exe`، SHA-256 `E41387831303F73C9BF0ADCDAF4D85214A8C56875A3E327A45CC73FF0BC639DA`.
- نصب روی همین دستگاه: Inno Setup exit code 0 و log برابر `Installation process succeeded`; نسخهٔ `C:\Program Files\AryanIQ\Rtl Terminal\RtlTerminal.exe` برابر `1.1.5+b833df92339560c923eb9677ce3897ceed3dd1a4`.
- میان‌بر دسکتاپ عمومی `C:\Users\Public\Desktop\Rtl Terminal.lnk` به exe نصب‌شده اشاره می‌کند و IconLocation صریحاً روی منبع آیکون همان exe تنظیم شده؛ اجرای برنامه از روی آیکون هنوز توسط کاربر تأیید نشده.
