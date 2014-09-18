using System.Windows;
using EUTECTclasses;
using System;

namespace CognexCleanup
{
    public class ApplicationStartup : Application
    {
        public MainWindow MyWindow { get; private set; }
        private iniFile settings = new iniFile(AppDomain.CurrentDomain.BaseDirectory + "\\Settings.ini");

        public ApplicationStartup()
            : base()
        { }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            MyWindow = new MainWindow();
            ProcessArgs(e.Args, true);

            MyWindow.Show();
            //MyWindow.WindowState = WindowState.Maximized;
        }

        public void ProcessArgs(string[] args, bool firstInstance)
        {
            //Process Command Line Arguments Here
            if (args.Length == 0)
            {
                //if no file is given via args get file from OpenFileDialog
                Microsoft.Win32.OpenFileDialog fileDlg = new Microsoft.Win32.OpenFileDialog();
                fileDlg.DefaultExt = "vpp";
                fileDlg.Filter = "VisionProFiles (*.vpp)|*.vpp";
                if (!String.IsNullOrEmpty(settings.ReadValue("FileDialog", "InitialDirectory")))
                {
                    fileDlg.InitialDirectory = settings.ReadValue("FileDialog", "InitialDirectory");
                }
                fileDlg.ShowDialog();
                
                MyWindow.OpenVPP(fileDlg.FileName);
            }
            
            else if (args.Length == 1)
            {
                MyWindow.OpenVPP(args[0]);
            }
        }
    }
}
