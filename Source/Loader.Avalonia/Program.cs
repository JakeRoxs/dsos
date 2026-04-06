using System;
using System.IO;

using Avalonia;

namespace Loader
{
  public static class Program
  {
    private static readonly string StartupLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RekindledServer",
        "loader-avalonia-startup.log");

    [STAThread]
    public static void Main(string[] args)
    {
      LogStartup($"Program.Main entry (args={string.Join(' ', args ?? Array.Empty<string>())}).");

      try
      {
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args ?? Array.Empty<string>());
      }
      catch (Exception ex)
      {
        LogStartup($"Program.Main fatal exception: {ex}");
        throw;
      }
    }

    public static AppBuilder BuildAvaloniaApp()
    {
      LogStartup("BuildAvaloniaApp invoked.");

      return AppBuilder.Configure<App>()
          .UsePlatformDetect()
          .WithInterFont()
          .LogToTrace();
    }

    private static void LogStartup(string message)
    {
      try
      {
        string? directory = Path.GetDirectoryName(StartupLogPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
          Directory.CreateDirectory(directory);
        }

        string line = $"[{DateTimeOffset.Now:O}] {message}{Environment.NewLine}";
        File.AppendAllText(StartupLogPath, line);
      }
      catch
      {
        // best-effort diagnostics only
      }
    }
  }
}
