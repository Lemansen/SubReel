using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

public static class GlobalExceptionHandler
{
    public static void Initialize(Action<string> logAction, Action<string> notifyAction)
    {
        // UI поток
        Application.Current.DispatcherUnhandledException += (s, e) =>
        {
            Handle(e.Exception, logAction, notifyAction);
            e.Handled = true;
        };

        // Task / async
        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            Handle(e.Exception, logAction, notifyAction);
            e.SetObserved();
        };

        // Любой поток
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            Handle(e.ExceptionObject as Exception, logAction, notifyAction);
        };
    }

    private static void Handle(Exception ex, Action<string> log, Action<string> notify)
    {
        if (ex == null) return;

        string message =
            "[CRASH] " + ex.GetType().Name + Environment.NewLine +
            ex.Message + Environment.NewLine +
            ex.StackTrace;

        try
        {
            log?.Invoke(message);
            WriteCrashToFile(message);
        }
        catch { }

        try
        {
            notify?.Invoke("Произошла ошибка. Подробности в логах.");
        }
        catch { }
    }

    private static void WriteCrashToFile(string text)
    {
        string path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SubReel",
            "crash.log");

        Directory.CreateDirectory(Path.GetDirectoryName(path));
        File.AppendAllText(path,
            $"[{DateTime.Now}] {text}{Environment.NewLine}{Environment.NewLine}");
    }
}