using System;
using System.IO;
using System.Threading;
using System.Windows;

namespace Snippetpal;

public partial class App : Application
{
    private Mutex? mutex;
    private EventWaitHandle? showEvent;
    private RegisteredWaitHandle? registeredWait;
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Snippetpal");
        for (int i = 0; i < e.Args.Length - 1; i++) if (e.Args[i] == "--data-dir") root = Path.GetFullPath(e.Args[i + 1]);
        if (Array.IndexOf(e.Args, "--self-test") >= 0) { int code = SelfTests.Run(root, Array.IndexOf(e.Args, "--no-clipboard") < 0); Shutdown(code); return; }
        if (Array.IndexOf(e.Args, "--render-preview") >= 0) { int code = PreviewRenderer.Run(root); Shutdown(code); return; }
        string identity = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(root.ToUpperInvariant())))[..16];
        mutex = new Mutex(true, @"Local\Snippetpal-" + identity, out bool first);
        showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\Snippetpal-show-" + identity);
        if (!first) { showEvent.Set(); Shutdown(); return; }
        try
        {
            var storage = new Storage(root); var library = new Library(storage); Themes.Apply(library.Data.Preferences.Theme);
            var window = new MainWindow(library); MainWindow = window;
            registeredWait = ThreadPool.RegisterWaitForSingleObject(showEvent, (_, _) => Dispatcher.BeginInvoke(window.ShowFromTray), null, Timeout.Infinite, false);
            window.Show();
            if (storage.LoadWarning != null) window.Report(storage.LoadWarning);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Snippetpal could not start.\n\n" + ex.Message, "Snippetpal", MessageBoxButton.OK, MessageBoxImage.Error); Shutdown(1);
        }
    }
    protected override void OnExit(ExitEventArgs e)
    { registeredWait?.Unregister(null); showEvent?.Dispose(); mutex?.Dispose(); base.OnExit(e); }
}
