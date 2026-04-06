using System;
using System.IO;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;

namespace Loader
{
  public partial class App : Application
  {
    private static readonly string StartupLogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "RekindledServer",
        "loader-avalonia-startup.log");

    public override void Initialize()
    {
      AvaloniaXamlLoader.Load(this);
#if DEBUG
      this.AttachDeveloperTools();
#endif
    }

    public override void OnFrameworkInitializationCompleted()
    {
      if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
      {
        MainWindow mainWindow = new MainWindow
        {
          WindowStartupLocation = WindowStartupLocation.CenterScreen,
          WindowState = WindowState.Normal,
          ShowInTaskbar = true
        };

        LogStartup("OnFrameworkInitializationCompleted: main window created.");

        desktop.MainWindow = mainWindow;
        LogStartup("OnFrameworkInitializationCompleted: desktop.MainWindow assigned.");

        desktop.Startup += (_, _) =>
        {
          LogStartup("Desktop lifetime Startup event fired.");
          EnsureMainWindowVisible(mainWindow, "Desktop.Startup");
        };

        mainWindow.Opened += (_, _) =>
        {
          LogStartup("MainWindow Opened event fired.");
          EnsureMainWindowVisible(mainWindow, "MainWindow.Opened");
        };

        mainWindow.Activated += (_, _) => LogStartup("MainWindow Activated event fired.");
        mainWindow.PositionChanged += (_, _) => LogWindowSnapshot(mainWindow, "MainWindow.PositionChanged");
        mainWindow.Closing += (_, _) => LogStartup("MainWindow Closing event fired.");

        Dispatcher.UIThread.Post(
            () => EnsureMainWindowVisible(mainWindow, "Post-MainWindow-Assign-Normal"),
            DispatcherPriority.Normal);

        Dispatcher.UIThread.Post(
            () => EnsureMainWindowVisible(mainWindow, "Post-MainWindow-Assign-Background"),
            DispatcherPriority.Background);
      }

      base.OnFrameworkInitializationCompleted();
    }

    private static void EnsureMainWindowVisible(Window mainWindow, string stage)
    {
      try
      {
        LogWindowSnapshot(mainWindow, $"{stage} (before)");

        if (mainWindow.WindowState == WindowState.Minimized)
        {
          mainWindow.WindowState = WindowState.Normal;
          LogStartup($"{stage}: window state was minimized and has been restored to Normal.");
        }

        if (!mainWindow.IsVisible)
        {
          mainWindow.Show();
          LogStartup($"{stage}: main window was not visible and Show() was invoked.");
        }

        mainWindow.Activate();

        // Toggle Topmost briefly to make sure the window is surfaced above other windows
        // when some compositors start it in the background.
        mainWindow.Topmost = true;
        mainWindow.Topmost = false;

        LogWindowSnapshot(mainWindow, $"{stage} (after)");
      }
      catch (Exception ex)
      {
        LogStartup($"{stage}: visibility enforcement failed: {ex}");
      }
    }

    private static void LogWindowSnapshot(Window window, string stage)
    {
      try
      {
        LogStartup(
            $"{stage}: visible={window.IsVisible}, active={window.IsActive}, state={window.WindowState}, " +
            $"position={window.Position.X},{window.Position.Y}, size={window.Bounds.Width:0.##}x{window.Bounds.Height:0.##}");
      }
      catch (Exception ex)
      {
        LogStartup($"{stage}: failed to capture window snapshot: {ex.Message}");
      }
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
        // Startup diagnostics are best-effort only.
      }
    }
  }
}
