using System;
using System.IO;
using System.Reflection;
using System.Runtime.Versioning;
using System.Windows.Forms;

[assembly: AssemblyTitle("VRCOSC Avatar Recorder")]
[assembly: AssemblyProduct("VRCOSC Avatar Recorder")]
[assembly: AssemblyDescription("VRCOSC Avatar Recorder is a tool for recording your VRChat avatar property changes and restoring them when reset.")]
[assembly: AssemblyCopyright("Copyright 2026 Jeremy Lam aka. Vistanz. Licensed under MIT.")]
[assembly: AssemblyVersion("0.0.1.0")]
[assembly: AssemblyFileVersion("0.0.1.0")]
[assembly: AssemblyInformationalVersion("0.0.1")]
[assembly: SupportedOSPlatform("windows")]

internal static class Program {
    [STAThread]
    private static void Main(string[] args) {
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        Application.ThreadException += OnThreadException;
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

        try {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
#pragma warning disable CA2000
            Application.Run(new MainForm());
#pragma warning restore CA2000
        } catch (Exception ex) {
            HandleFatalException(LocalizationManager.Get("Program.Error.ApplicationStart"), ex);
        }
    }

    private static void OnThreadException(object? sender, System.Threading.ThreadExceptionEventArgs e) {
        HandleFatalException(LocalizationManager.Get("Program.Error.UIThreadException"), e.Exception);
    }

    private static void OnUnhandledException(object? sender, UnhandledExceptionEventArgs e) {
        Exception exception = e.ExceptionObject as Exception ?? new Exception(LocalizationManager.Get("Program.Error.UnknownUnhandled"));
        HandleFatalException(LocalizationManager.Get("Program.Error.FatalUnhandled"), exception);
    }

    private static void HandleFatalException(string title, Exception exception) {
        try {
            string message = title + Environment.NewLine + Environment.NewLine + exception;
            string logPath = Path.Combine(AppContext.BaseDirectory, "startup-error.log");
            File.AppendAllText(logPath, $"[{DateTimeOffset.UtcNow.ToString("O")}] {message}{Environment.NewLine}{Environment.NewLine}");
            MessageBox.Show(message, LocalizationManager.Get("App.Title"), MessageBoxButtons.OK, MessageBoxIcon.Error);
        } catch {
            // Nothing else to do if diagnostics fail.
        }
    }

}
