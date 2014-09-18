using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Diagnostics;
using EUTECTclasses;
using Cognex.VisionPro.QuickBuild;
using Cognex.VisionPro;
using System.Threading;
using Cognex.VisionPro.ToolGroup;
using System.Windows.Forms.Integration;


namespace CognexCleanup
{
    /// <summary>
    /// Interaktionslogik für MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private iniFile settings = new iniFile(AppDomain.CurrentDomain.BaseDirectory + "\\Settings.ini");
        private TraceListener_String trace;

        private string VisionProFilePath;
        Thread CognexLoader;
        CogJobManager myJobManager;
        CogJob myJob;
        CogJobIndependent myIndependentJob;
        
        CogToolGroup tmpToolGroup = null;
        String tmpProcessingString = "";

        CogJobManagerEdit myJobManagerEdit;

        public MainWindow()
        {
            InitializeComponent();

            #region Tracing + Trace-Parameters
            trace = new TraceListener_String(txt_TraceLog, true);
            trace.Logname = "ECC";
            Trace.Listeners.Add(trace);

            try
            {
                trace.IsLogging = Convert.ToBoolean(settings.CreateOrGetValue("ECC", "TraceActive", "TRUE")); ;
                trace.EraseMode = TraceEraseMode.Time;
                trace.EraseParameter = 10;
            }
            catch (Exception) { }

            trace.eraseLogFiles();
            Trace.WriteLine("EUTECT CognexCleanup (ECC) started, Version: " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version);
            #endregion
        }

        internal void OpenVPP(string VisionProFilePath)
        {
            Dispatcher.Invoke(new Action(() => { brd_Buttons.IsEnabled = false; brd_Loading.Visibility = Visibility.Visible; Panel.SetZIndex(brd_Loading, int.MaxValue); }));
            
            if (System.IO.File.Exists(VisionProFilePath))
            {
                if (VisionProFilePath.EndsWith(".vpp"))
                {
                    settings.WriteValue("FileDialog", "InitialDirectory", System.IO.Path.GetDirectoryName(VisionProFilePath));
                    Trace.WriteLine("Open VisionProFile: " + VisionProFilePath);
                    this.VisionProFilePath = VisionProFilePath;
                    CognexLoader = new Thread(loadCognex);
                    CognexLoader.Start();
                    return;
                }
            }
            Trace.WriteLine("No VisionProFile selected.");
        }

        void loadCognex()
        {
            try
            {
                myJobManager = (CogJobManager)CogSerializer.LoadObjectFromFile(VisionProFilePath);
                myJob = myJobManager.Job(0);
                myIndependentJob = myJob.OwnedIndependent;

                //flush queues
                //myJobManager.UserQueueFlush();
                //myJobManager.FailureQueueFlush();
                //myJob.ImageQueueFlush();
                //myJob.ResetAllStatistics();
                //myIndependentJob.RealTimeQueueFlush();

                Trace.WriteLine("VisionProFile loaded.");
                Dispatcher.Invoke(new Action(() => { brd_Buttons.IsEnabled = true; brd_Loading.Visibility = Visibility.Collapsed; Panel.SetZIndex(brd_Loading, int.MinValue); }));
            }
            catch (Exception ex)
            {
                Trace.WriteLine("Vision-Pro not ready! Exception:" + ex.Message);
            }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            #region CognexShutDown
            try
            {
                //Close all open Framegrabbers due to RuntimeError R6025
                CogFrameGrabbers frameGrabbers = new CogFrameGrabbers();
                foreach (ICogFrameGrabber fg in frameGrabbers)
                    fg.Disconnect(false); 

                //End Starting of Cognex in separate Thread
                if (CognexLoader != null) CognexLoader.Abort();
                // Be sure to shudown the CogJobManager!!
                if (myJobManager != null) myJobManager.Shutdown();
            }
            catch (Exception ex)
            {
                Trace.WriteLine("Vision-Pro shutdown. Excepiton:" + ex.Message);
            }
            #endregion
        }

        

        void SaveCognexFile()
        {
            try
            {
                if (Convert.ToBoolean(settings.CreateOrGetValue("FileDialog", "CreateBackup", "True"))) 
                {
                    String BackupFileName = VisionProFilePath + DateTime.Now.ToString("_yyyyMMdd_HHmmss");
                    Trace.WriteLine("Creating backup: " + BackupFileName);
                    System.IO.File.Move(VisionProFilePath, BackupFileName);
                }
                CogSerializer.SaveObjectToFile(myJobManager, VisionProFilePath);
                Trace.WriteLine("File saved: " + VisionProFilePath);
            }
            catch (Exception ex)
            {
                Trace.WriteLine("Error saving file. Exception: " + ex.Message);
            }
        }

        private void SetRelease(CogToolGroup toolGroup)
        {
            foreach (ICogTool tool in toolGroup.Tools)
            {
                if ((tmpToolGroup = (tool as CogToolGroup)) != null)
                {
                    //Trace.WriteLine("Group:" + tmpToolGroup.Name);

                    if (tmpToolGroup.Script != null && tmpToolGroup.Script.CompileDebug)
                    {
                        Trace.WriteLine("Change Script to Release: " + tmpToolGroup.Name );
                        tmpToolGroup.Script.CompileDebug = false;
                    }
                    SetRelease(tmpToolGroup);
                }
                else
                {
                    //Trace.WriteLine(" Tool: " + tool.Name);
                }
            }
        }

        private void DeactivateImages(CogToolGroup toolGroup)
        {
            Trace.WriteLine("DeactivateImages");
            foreach (ICogTool tool in toolGroup.Tools)
            {
                //CogToolGroup
                if ((tmpToolGroup = (tool as CogToolGroup)) != null)
                {
                    tmpProcessingString = "Processing Group: " + tool.Name;
                    //recursively check all ToolBlocks/ToolGroups
                    DeactivateImages(tmpToolGroup);
                }
                else
                {
                    tmpProcessingString = "-> Processing: " + tool.Name;
                    
                    //CogBlobTool
                    if (tool as Cognex.VisionPro.Blob.CogBlobTool != null)
                    {
                        Cognex.VisionPro.Blob.CogBlobTool Blob = tool as Cognex.VisionPro.Blob.CogBlobTool;
                        Blob.CurrentRecordEnable = 0;
                        Blob.LastRunRecordDiagEnable = 0;
                        Blob.LastRunRecordEnable = 0;
                    }

                    //CogPMAlignTool
                    else if (tool as Cognex.VisionPro.PMAlign.CogPMAlignTool != null)
                    {
                        Cognex.VisionPro.PMAlign.CogPMAlignTool PMAlign = tool as Cognex.VisionPro.PMAlign.CogPMAlignTool;
                        PMAlign.CurrentRecordEnable = 0;
                        PMAlign.LastRunRecordDiagEnable = 0;
                        PMAlign.LastRunRecordEnable = 0;
                    }

                    //CogSearchMaxTool
                    else if (tool as Cognex.VisionPro.SearchMax.CogSearchMaxTool != null)
                    {
                        Cognex.VisionPro.SearchMax.CogSearchMaxTool SearchMax = tool as Cognex.VisionPro.SearchMax.CogSearchMaxTool;
                        SearchMax.CurrentRecordEnable = 0;
                        SearchMax.LastRunRecordDiagEnable = 0;
                        SearchMax.LastRunRecordEnable = 0;
                    }

                    //CogFixtureTool
                    else if (tool as Cognex.VisionPro.CalibFix.CogFixtureTool != null)
                    {
                        Cognex.VisionPro.CalibFix.CogFixtureTool FixtureTool = tool as Cognex.VisionPro.CalibFix.CogFixtureTool;
                        FixtureTool.CurrentRecordEnable = 0;
                        FixtureTool.LastRunRecordDiagEnable = 0;
                        FixtureTool.LastRunRecordEnable = 0;
                    }

                    //CogHistogramTool
                    else if (tool as Cognex.VisionPro.ImageProcessing.CogHistogramTool != null)
                    {
                        Cognex.VisionPro.ImageProcessing.CogHistogramTool Histogram = tool as Cognex.VisionPro.ImageProcessing.CogHistogramTool;
                        Histogram.CurrentRecordEnable = 0;
                        Histogram.LastRunRecordDiagEnable = 0;
                        Histogram.LastRunRecordEnable = 0;
                    }

                    //CogFindLineTool
                    else if (tool as Cognex.VisionPro.Caliper.CogFindLineTool != null)
                    {
                        Cognex.VisionPro.Caliper.CogFindLineTool FindLine = tool as Cognex.VisionPro.Caliper.CogFindLineTool;
                        FindLine.CurrentRecordEnable = 0;
                        FindLine.LastRunRecordDiagEnable = 0;
                        FindLine.LastRunRecordEnable = 0;
                    }
                    
                    //CogIPOneImageTool
                    else if (tool as Cognex.VisionPro.ImageProcessing.CogIPOneImageTool != null)
                    {
                        Cognex.VisionPro.ImageProcessing.CogIPOneImageTool oneImageTool = tool as Cognex.VisionPro.ImageProcessing.CogIPOneImageTool;
                        oneImageTool.CurrentRecordEnable = 0;
                        oneImageTool.LastRunRecordDiagEnable = 0;
                        oneImageTool.LastRunRecordEnable = 0;
                    }

                    //CogInputImageTool (Nicht beachten, da Kamera)
                    //CogPixelMapTool
                    //CogImageConvertTool

                    else
                    {
                        tmpProcessingString = String.Format("   Skipping: {0}({1})", tool.Name, tool.GetType().ToString());
                    //    //Breakpoint for unused tools :)
                    //    Trace.Write("Skip..." );
                    }    
                }
                Trace.WriteLine(tmpProcessingString);
            }
        }

        private void ReactivateImages(CogToolGroup toolGroup)
        {
            Trace.WriteLine("ReactivateImages");
            foreach (ICogTool tool in toolGroup.Tools)
            {
                //CogToolGroup
                if ((tmpToolGroup = (tool as CogToolGroup)) != null)
                {
                    tmpProcessingString = "Processing Group: " + tool.Name;
                    //recursively check all ToolBlocks/ToolGroups
                    ReactivateImages(tmpToolGroup);
                }
                else
                {
                    tmpProcessingString = "-> Processing: " + tool.Name;

                    //CogBlobTool
                    if (tool as Cognex.VisionPro.Blob.CogBlobTool != null)
                    {
                        Cognex.VisionPro.Blob.CogBlobTool Blob = tool as Cognex.VisionPro.Blob.CogBlobTool;
                        Blob.CurrentRecordEnable = Cognex.VisionPro.Blob.CogBlobCurrentRecordConstants.All;
                        Blob.LastRunRecordDiagEnable = Cognex.VisionPro.Blob.CogBlobLastRunRecordDiagConstants.All;
                        Blob.LastRunRecordEnable = Cognex.VisionPro.Blob.CogBlobLastRunRecordConstants.All;
                    }

                    //CogPMAlignTool
                    else if (tool as Cognex.VisionPro.PMAlign.CogPMAlignTool != null)
                    {
                        Cognex.VisionPro.PMAlign.CogPMAlignTool PMAlign = tool as Cognex.VisionPro.PMAlign.CogPMAlignTool;
                        PMAlign.CurrentRecordEnable = Cognex.VisionPro.PMAlign.CogPMAlignCurrentRecordConstants.All;
                        PMAlign.LastRunRecordDiagEnable = Cognex.VisionPro.PMAlign.CogPMAlignLastRunRecordDiagConstants.All;
                        PMAlign.LastRunRecordEnable = Cognex.VisionPro.PMAlign.CogPMAlignLastRunRecordConstants.All;
                    }

                    //CogSearchMaxTool
                    else if (tool as Cognex.VisionPro.SearchMax.CogSearchMaxTool != null)
                    {
                        Cognex.VisionPro.SearchMax.CogSearchMaxTool SearchMax = tool as Cognex.VisionPro.SearchMax.CogSearchMaxTool;
                        SearchMax.CurrentRecordEnable = Cognex.VisionPro.SearchMax.CogSearchMaxCurrentRecordConstants.All;
                        SearchMax.LastRunRecordDiagEnable = Cognex.VisionPro.SearchMax.CogSearchMaxLastRunRecordDiagConstants.All;
                        SearchMax.LastRunRecordEnable = Cognex.VisionPro.SearchMax.CogSearchMaxLastRunRecordConstants.All;
                    }

                    //CogFixtureTool
                    else if (tool as Cognex.VisionPro.CalibFix.CogFixtureTool != null)
                    {
                        Cognex.VisionPro.CalibFix.CogFixtureTool FixtureTool = tool as Cognex.VisionPro.CalibFix.CogFixtureTool;
                        FixtureTool.CurrentRecordEnable = Cognex.VisionPro.CalibFix.CogFixtureCurrentRecordConstants.All;
                        FixtureTool.LastRunRecordDiagEnable = Cognex.VisionPro.CalibFix.CogFixtureLastRunRecordDiagConstants.All;
                        FixtureTool.LastRunRecordEnable = Cognex.VisionPro.CalibFix.CogFixtureLastRunRecordConstants.All;
                    }

                    //CogHistogramTool
                    else if (tool as Cognex.VisionPro.ImageProcessing.CogHistogramTool != null)
                    {
                        Cognex.VisionPro.ImageProcessing.CogHistogramTool Histogram = tool as Cognex.VisionPro.ImageProcessing.CogHistogramTool;
                        Histogram.CurrentRecordEnable = Cognex.VisionPro.ImageProcessing.CogHistogramCurrentRecordConstants.All;
                        Histogram.LastRunRecordDiagEnable = Cognex.VisionPro.ImageProcessing.CogHistogramLastRunRecordDiagConstants.All;
                        Histogram.LastRunRecordEnable = Cognex.VisionPro.ImageProcessing.CogHistogramLastRunRecordConstants.All;
                    }

                    //CogFindLineTool
                    else if (tool as Cognex.VisionPro.Caliper.CogFindLineTool != null)
                    {
                        Cognex.VisionPro.Caliper.CogFindLineTool FindLine = tool as Cognex.VisionPro.Caliper.CogFindLineTool;
                        FindLine.CurrentRecordEnable = Cognex.VisionPro.Caliper.CogFindLineCurrentRecordConstants.All;
                        FindLine.LastRunRecordDiagEnable = Cognex.VisionPro.Caliper.CogFindLineLastRunRecordDiagConstants.All;
                        FindLine.LastRunRecordEnable = Cognex.VisionPro.Caliper.CogFindLineLastRunRecordConstants.All;
                    }

                    //CogIPOneImageTool
                    else if (tool as Cognex.VisionPro.ImageProcessing.CogIPOneImageTool != null)
                    {
                        Cognex.VisionPro.ImageProcessing.CogIPOneImageTool oneImageTool = tool as Cognex.VisionPro.ImageProcessing.CogIPOneImageTool;
                        oneImageTool.CurrentRecordEnable = Cognex.VisionPro.ImageProcessing.CogIPOneImageCurrentRecordConstants.All;
                        oneImageTool.LastRunRecordDiagEnable = Cognex.VisionPro.ImageProcessing.CogIPOneImageLastRunRecordDiagConstants.All;
                        oneImageTool.LastRunRecordEnable = Cognex.VisionPro.ImageProcessing.CogIPOneImageLastRunRecordConstants.All;
                    }

                    //CogInputImageTool (Nicht beachten, da Kamera)
                    //CogPixelMapTool
                    //CogImageConvertTool

                    else
                    {
                        tmpProcessingString = String.Format("   Skipping: {0}({1})", tool.Name, tool.GetType().ToString());
                        //    //Breakpoint for unused tools :)
                        //    Trace.Write("Skip..." );
                    }
                }
                Trace.WriteLine(tmpProcessingString);
            }
        }

        private void btn_setRelease_Click(object sender, RoutedEventArgs e)
        {
            Trace.WriteLine("Change all scripts to release...");
            if (myJob == null) return;

            if (myJob.JobScript != null)
            {
                if (myJob.JobScript.CompileDebug)
                {
                    Trace.WriteLine("Change JobScript to Release");
                    myJob.JobScript.CompileDebug = false;
                }
            }

            SetRelease(myJob.VisionTool as CogToolGroup);
            SaveCognexFile();
        }

        private void btn_disableImages_Click(object sender, RoutedEventArgs e)
        {
            Trace.WriteLine("Disable all additional images...");
            if (myJob == null) return;
           
            DeactivateImages(myJob.VisionTool as CogToolGroup);
            SaveCognexFile();
        }

        private void btn_enableImages_Click(object sender, RoutedEventArgs e)
        {
            Trace.WriteLine("Enable all additional images...");
            if (myJob == null) return;

            ReactivateImages(myJob.VisionTool as CogToolGroup);
            SaveCognexFile();
        }

        private void btn_openQuickBuild_Click(object sender, RoutedEventArgs e)
        {
            Trace.WriteLine("Open current project in QuickBuild ...");
            SaveCognexFile();

            if (myJobManagerEdit == null)
            {
                if (myJobManager != null)
                {
                    try
                    {
                        myJobManagerEdit = new CogJobManagerEdit();
                        myJobManagerEdit.ShowLocalizationTab = false;
                        myJobManagerEdit.Subject = myJobManager;
                    }
                    catch { }
                }
            }

            FormsHoster formHoster = new FormsHoster();
            formHoster.FormHost.Child = myJobManagerEdit;
            formHoster.WindowState = System.Windows.WindowState.Maximized;
            formHoster.ShowDialog();
            formHoster = null;

            myJobManagerEdit = null;
        }
    }
}
