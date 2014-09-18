using System;
using System.Linq;
using Microsoft.VisualBasic.ApplicationServices;

namespace CognexCleanup
{
    public sealed class ApplicationSingleInstanceManager : WindowsFormsApplicationBase
    {
        [STAThread]
        public static void Main(string[] args)
        {
            (new ApplicationSingleInstanceManager()).Run(args);
        }

        public ApplicationSingleInstanceManager()
        {
            IsSingleInstance = true;
        }

        public ApplicationStartup App
        {
            get;
            private set;
        }

        protected override bool OnStartup(StartupEventArgs e)
        {
            App = new ApplicationStartup();
            App.Run();
            return false;
        }

        protected override void OnStartupNextInstance(
          StartupNextInstanceEventArgs eventArgs)
        {
            base.OnStartupNextInstance(eventArgs);
            if (App.MyWindow != null)
            {
                if (!App.MyWindow.IsVisible)
                {
                    App.MyWindow.WindowState = System.Windows.WindowState.Maximized;
                    App.MyWindow.Show();
                }
                App.MyWindow.Activate();
                App.ProcessArgs(eventArgs.CommandLine.ToArray(), false);
            }
            else
            {
                using (System.IO.StreamWriter writer = new System.IO.StreamWriter(AppDomain.CurrentDomain.BaseDirectory + "startuperror.txt", true))
                {
                    writer.WriteLine(DateTime.Now.ToString() + ": App.MyWindow not yet set in ApplicationSingleInstanceManager.OnStartupNextInstance()");
                }
            }
        }
    }
}