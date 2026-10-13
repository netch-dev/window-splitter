using System.Text.Json;

namespace WindowSplitter;

/// <summary>One window in a workspace. X/Y/W/H are fractions (0-1) of the monitor work area.</summary>
public class Slot
{
    public string Process { get; set; } = "";       // process name without .exe, e.g. "chrome"
    public string TitleContains { get; set; } = ""; // optional filter, e.g. "YouTube"
    public string Launch { get; set; } = "";        // optional full path/command, run if the window isn't open
    public string Args { get; set; } = "";
    public double X { get; set; }
    public double Y { get; set; }
    public double W { get; set; } = 0.5;
    public double H { get; set; } = 1;
}

public class Workspace
{
    public string Name { get; set; } = "";
    public List<Slot> Slots { get; set; } = new();
}

public class Config
{
    public List<Workspace> Workspaces { get; set; } = new();

    static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };
    public static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WindowSplitter");
    public static string FilePath => Path.Combine(Dir, "config.json");

    public static Config Load()
    {
        try { if (File.Exists(FilePath)) return JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath)) ?? new(); }
        catch { }
        return new();
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Opts));
    }
}
