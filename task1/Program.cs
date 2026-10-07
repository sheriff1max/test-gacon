using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using static User32;

var cpuSettings = new Dictionary<string, string>
{
    ["ResolutionSizeX"] = "1280", ["ResolutionSizeY"] = "720",
    ["sg.ResolutionQuality"] = "100",
    ["sg.ViewDistanceQuality"] = "0", ["sg.AntiAliasingQuality"] = "0",
    ["sg.ShadowQuality"] = "0", ["sg.GlobalIlluminationQuality"] = "0",
    ["sg.ReflectionQuality"] = "0", ["sg.PostProcessQuality"] = "0",
    ["sg.TextureQuality"] = "0", ["sg.EffectsQuality"] = "0",
    ["sg.FoliageQuality"] = "0", ["sg.ShadingQuality"] = "0",
    ["sg.RayTracingQuality"] = "0",
    ["UpscaleMode"] = "0",
    ["bUseVSync"] = "False", ["FrameRateLimit"] = "60.000000",
};
var gpuSettings = new Dictionary<string, string>
{
    ["ResolutionSizeX"] = "1920", ["ResolutionSizeY"] = "1080",
    ["sg.ResolutionQuality"] = "100",
    ["sg.ViewDistanceQuality"] = "1", ["sg.AntiAliasingQuality"] = "1",
    ["sg.ShadowQuality"] = "1", ["sg.GlobalIlluminationQuality"] = "1",
    ["sg.ReflectionQuality"] = "1", ["sg.PostProcessQuality"] = "1",
    ["sg.TextureQuality"] = "1", ["sg.EffectsQuality"] = "1",
    ["sg.FoliageQuality"] = "1", ["sg.ShadingQuality"] = "1",
    ["sg.RayTracingQuality"] = "0",
    ["UpscaleMode"] = "0",
    ["bUseVSync"] = "False", ["FrameRateLimit"] = "60.000000",
};

Console.OutputEncoding = Encoding.UTF8;

if (args.Length > 0 && args[0] == "calibrate") { Calibrate(); return 0; }

var (btnX, btnY) = (C.BtnX, C.BtnY);
if (File.Exists(C.BtnFile))
{
    var j = JsonSerializer.Deserialize<Dictionary<string, double>>(File.ReadAllText(C.BtnFile))!;
    (btnX, btnY) = (j["x"], j["y"]);
    Console.WriteLine($"Кнопка: из button.json (x={btnX:F3}, y={btnY:F3})");
}
else Console.WriteLine("Кнопка: дефолтные координаты (если клик мимо — выполните WukongBench calibrate)");

var hw = HardwareInfo();
var cpuResult = RunPass("CPU", cpuSettings, btnX, btnY);
var gpuResult = RunPass("GPU", gpuSettings, btnX, btnY);

var report = Report(hw, cpuSettings, cpuResult, gpuSettings, gpuResult);
Console.WriteLine("\n" + report);
File.WriteAllText("wukong_benchmark_report.md", report);
Console.WriteLine("Отчёт сохранён: wukong_benchmark_report.md");
return 0;

static Dictionary<string, double> RunPass(string name, Dictionary<string, string> settings, double btnX, double btnY)
{
    Console.WriteLine($"[{name}] Применяем настройки...");
    ApplySettings(settings);

    Console.WriteLine($"[{name}] Запускаем Benchmark Tool...");
    using var p = Launch();
    var passStart = DateTime.UtcNow;

    Console.WriteLine($"[{name}] Бенчмарк идёт, ждём результаты в логе...");
    var metrics = WaitAndClick(p, btnX, btnY, passStart);
    p.CloseMainWindow();
    if (!p.WaitForExit(5000)) p.Kill();
    Console.WriteLine($"[{name}] Готово: {metrics.Count} метрик.");
    if (metrics.Count == 0) Diagnostics(p, passStart);
    return metrics;
}

static void ApplySettings(Dictionary<string, string> kv)
{
    var paths = new List<string> { Path.Combine(C.SavedDir, "Config", "Windows", "GameUserSettings.ini") };
    var install = FindInstallDir();
    if (install != null) paths.Add(Path.Combine(install, "b1", "Saved", "Config", "Windows", "GameUserSettings.ini"));
    foreach (var path in paths)
    {
        var lines = File.Exists(path) ? File.ReadAllLines(path).ToList() : new List<string>();
        const string section = "[/Script/Engine.GameUserSettings]";
        if (!lines.Contains(section)) lines.Insert(0, section);
        int first = lines.IndexOf(section);
        int end = lines.FindIndex(first + 1, l => l.StartsWith("["));
        if (end < 0) end = lines.Count;
        foreach (var (k, v) in kv)
        {
            int i = lines.FindIndex(first, end - first, l => l.StartsWith(k + "=", StringComparison.OrdinalIgnoreCase));
            if (i >= 0) lines[i] = $"{k}={v}";
            else { lines.Insert(end, $"{k}={v}"); end++; }
        }
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path, lines);
    }
}

static string? FindInstallDir()
{
    var steam = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
    if (steam is null) return null;
    var lf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
    if (!File.Exists(lf)) return null;
    foreach (Match m in Regex.Matches(File.ReadAllText(lf), "\"path\"\\s+\"(.+?)\""))
    {
        var acf = Path.Combine(m.Groups[1].Value, "steamapps", $"appmanifest_{C.AppId}.acf");
        if (!File.Exists(acf)) continue;
        var dir = Regex.Match(File.ReadAllText(acf), "\"installdir\"\\s+\"(.+?)\"").Groups[1].Value;
        return Path.Combine(m.Groups[1].Value, "steamapps", "common", dir);
    }
    return null;
}

static Process Launch()
{
    Process.Start(new ProcessStartInfo($"steam://rungameid/{C.AppId}//") { UseShellExecute = true });
    for (int t = 0; t < 120; t++)
    {
        var p = Process.GetProcessesByName(C.ProcName).FirstOrDefault();
        if (p != null) return p;
        Thread.Sleep(1000);
    }
    throw new InvalidOperationException("Процесс Benchmark Tool не найден (Steam запущен? игра установлена?)");
}

static Dictionary<string, double> WaitAndClick(Process p, double bx, double by, DateTime passStart)
{
    var log = Path.Combine(C.SavedDir, "Logs", "b1.log");
    long offset = File.Exists(log) ? new FileInfo(log).Length : 0;
    var sb = new StringBuilder();
    var lastClick = passStart;   // первый клик — через 20 с после старта (не в сплэш)
    int clicks = 0;

    while ((DateTime.UtcNow - passStart).TotalMinutes < 15)
    {
        Thread.Sleep(2000);
        if (File.Exists(log))
            using (var fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                fs.Seek(offset, SeekOrigin.Begin);
                using var rd = new StreamReader(fs);
                sb.Append(rd.ReadToEnd());
                offset = fs.Length;
            }
        var metrics = ParseMetrics(sb.ToString());
        if (metrics.Count > 0) return metrics;
        if (p.HasExited) break;

        bool windowUp = p.MainWindowHandle != IntPtr.Zero;
        bool due = (DateTime.UtcNow - lastClick).TotalSeconds > 20;
        if (windowUp && due && clicks < C.MaxClicks)
        {
            Click(p, bx, by);
            clicks++;
            lastClick = DateTime.UtcNow;
            Console.WriteLine($"  клик по START TEST #{clicks}");
        }
    }
    return ParseMetrics(sb.ToString());
}

static void Click(Process p, double bx, double by)
{
    keybd_event(0x12, 0, 0, UIntPtr.Zero);
    SetForegroundWindow(p.MainWindowHandle);
    keybd_event(0x12, 0, 2, UIntPtr.Zero);
    GetWindowRect(p.MainWindowHandle, out var r);
    SetCursorPos(r.Left + (int)((r.Right - r.Left) * bx), r.Top + (int)((r.Bottom - r.Top) * by));
    mouse_event(2, 0, 0, 0, UIntPtr.Zero);
    mouse_event(4, 0, 0, 0, UIntPtr.Zero);
}

static void Calibrate()
{
    Console.WriteLine("1) Запустите Benchmark Tool вручную (или дождитесь меню).");
    Console.WriteLine("2) Наведите курсор мыши на кнопку START TEST.");
    Console.WriteLine("3) Нажмите Enter в этом окне.");
    Console.ReadLine();
    var p = Process.GetProcessesByName(C.ProcName).FirstOrDefault()
        ?? throw new InvalidOperationException("Процесс Benchmark Tool не найден.");
    GetCursorPos(out var pt);
    GetWindowRect(p.MainWindowHandle, out var r);
    var x = (pt.X - r.Left) / (double)(r.Right - r.Left);
    var y = (pt.Y - r.Top) / (double)(r.Bottom - r.Top);
    File.WriteAllText(C.BtnFile, JsonSerializer.Serialize(new { x = Math.Round(x, 4), y = Math.Round(y, 4) }));
    Console.WriteLine($"Сохранено в button.json: x={x:F3}, y={y:F3}. Основной режим будет бить точно.");
}

static void Diagnostics(Process p, DateTime passStart)
{
    Console.WriteLine("--- ДИАГНОСТИКА (метрики не найдены) ---");
    Console.WriteLine($"Процесс: {(p.HasExited ? "завершился досрочно (краш?)" : "жив")}, окно: {(p.MainWindowHandle != IntPtr.Zero ? "есть" : "нет")}");
    var roots = new List<string> { C.SavedDir };
    var install = FindInstallDir();
    if (install != null) roots.Add(Path.Combine(install, "b1", "Saved"));
    foreach (var root in roots.Where(Directory.Exists))
        foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
        {
            var t = File.GetLastWriteTimeUtc(f);
            if (t > passStart) Console.WriteLine($"  изменён: {f} ({t:HH:mm:ss})");
        }
    var log = Path.Combine(C.SavedDir, "Logs", "b1.log");
    if (File.Exists(log))
    {
        Console.WriteLine("  хвост лога (строки про bench/fps/frame):");
        foreach (var line in File.ReadAllLines(log).Where(l =>
                     Regex.IsMatch(l, "bench|fps|frame", RegexOptions.IgnoreCase)).TakeLast(15))
            Console.WriteLine("    " + line);
    }
    Console.WriteLine("Пришлите этот блок — по нему видно, куда инструмент кладёт результаты.");
}

static Dictionary<string, double> ParseMetrics(string text)
{
    var res = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
    foreach (Match m in Regex.Matches(text, @"([A-Za-z0-9_ %]*?(?:FPS|Fps|fps|FrameRate|Framerate)[A-Za-z0-9_ %]*?)\s*[:=""]+\s*(\d+(?:\.\d+)?)"))
        res.TryAdd(m.Groups[1].Value.Trim(), double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
    return res;
}

static string HardwareInfo()
{
    string Wmi(string q, string prop)
    {
        using var s = new ManagementObjectSearcher(q);
        return string.Join(", ", s.Get().Cast<ManagementBaseObject>()
            .Select(o => o[prop]?.ToString()?.Trim()).Where(v => !string.IsNullOrEmpty(v)));
    }
    var ramGb = new ManagementObjectSearcher("SELECT Capacity FROM Win32_PhysicalMemory").Get()
        .Cast<ManagementBaseObject>().Sum(o => Convert.ToDouble(o["Capacity"]) / (1024 * 1024 * 1024));
    return $"CPU: {Wmi("SELECT Name FROM Win32_Processor", "Name")}\n" +
           $"GPU: {Wmi("SELECT Name FROM Win32_VideoController", "Name")}\n" +
           $"RAM: {ramGb:F0} GB\n" +
           $"OS:  {Wmi("SELECT Caption FROM Win32_OperatingSystem", "Caption")}";
}

static string Report(string hw, Dictionary<string, string> cpuCfg, Dictionary<string, double> cpuRes,
                     Dictionary<string, string> gpuCfg, Dictionary<string, double> gpuRes)
{
    static string Fmt(Dictionary<string, double> m) =>
        m.Count == 0 ? "(метрики не найдены в логе)" :
        string.Join("\n", m.Select(kv => $"  {kv.Key}: {kv.Value:F2}"));
    static string Cfg(Dictionary<string, string> c) =>
        string.Join("\n", c.Select(kv => $"  {kv.Key}={kv.Value}"));
    return $"# Black Myth: Wukong Benchmark — отчёт {DateTime.Now:yyyy-MM-dd HH:mm}\n\n" +
           $"## Система\n{hw}\n\n" +
           $"## CPU-тест\nНастройки:\n{Cfg(cpuCfg)}\nРезультат:\n{Fmt(cpuRes)}\n\n" +
           $"## GPU-тест\nНастройки:\n{Cfg(gpuCfg)}\nРезультат:\n{Fmt(gpuRes)}\n";
}

static class C
{
    public const int AppId = 3132990;
    public const string ProcName = "b1-Win64-Shipping";
    public const double BtnX = 0.50, BtnY = 0.86; // дефолт: доля окна кнопки START TEST
    public const int MaxClicks = 12;              // максимум кликов за проход (~4 мин)
    public const string BtnFile = "button.json";
    public static readonly string SavedDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "b1", "Saved");
}

struct POINT { public int X, Y; }
struct RECT { public int Left, Top, Right, Bottom; }

static class User32
{
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
    [DllImport("user32.dll")] public static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] public static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);
}
