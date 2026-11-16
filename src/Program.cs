using System;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("메모도우미")]
[assembly: AssemblyProduct("메모도우미")]
[assembly: AssemblyVersion("0.1.0.0")]
[assembly: AssemblyFileVersion("0.1.0.0")]
[assembly: AssemblyInformationalVersion("0.1.0")]

namespace MemoDoumi
{
    internal static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {

            if (args.Length == 2 && args[0] == "--export-icon")
            {
                Sprites.ExportIcon(args[1]);
                return 0;
            }

            bool created;
            using (new Mutex(true, @"Local\MemoDoumi.SingleInstance", out created))
            {
                if (!created && args.Length == 1 && args[0] == "--quit")
                {
                    int quit = Native.RegisterWindowMessage(App.QuitMessageName);
                    IntPtr target = Native.FindWindow(null, App.MascotTitle);
                    if (quit != 0 && target != IntPtr.Zero) Native.PostMessage(target, quit, IntPtr.Zero, IntPtr.Zero);
                    return 0;
                }
                if (created && args.Length == 1 && args[0] == "--quit") return 0;
                if (!created)
                {

                    int msg = Native.RegisterWindowMessage(App.ShowMessageName);
                    IntPtr running = Native.FindWindow(null, App.MascotTitle);
                    if (msg != 0 && running != IntPtr.Zero) Native.PostMessage(running, msg, IntPtr.Zero, IntPtr.Zero);
                    return 0;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (s, e) => App.Log(e.Exception);
                AppDomain.CurrentDomain.UnhandledException += (s, e) => App.Log(e.ExceptionObject as Exception);

                new App().Run();
            }
            return 0;
        }
    }
}
