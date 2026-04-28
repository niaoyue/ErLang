using System.Windows.Forms;

namespace OwnDesk.Client;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, eventArgs) =>
            ClientLog.WriteException("UI thread exception", eventArgs.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
        {
            if (eventArgs.ExceptionObject is Exception exception)
            {
                ClientLog.WriteException("Unhandled exception", exception);
            }
        };
        TaskScheduler.UnobservedTaskException += (_, eventArgs) =>
        {
            ClientLog.WriteException("Unobserved task exception", eventArgs.Exception);
            eventArgs.SetObserved();
        };
        ClientLog.Write("Client starting");
        Application.Run(new MainForm());
        ClientLog.Write("Client exited");
    }
}
