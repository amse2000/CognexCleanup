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
    enum ModificationMode
    {
        disable = 0, enableALL, standard
    }

    /// <summary>
    /// Interaktionslogik für MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private iniFile settings = new iniFile(AppDomain.CurrentDomain.BaseDirectory + "\\Settings.ini");
        private TraceListener_String trace;

        private string visionProFilePath;
        Thread cognexLoader;
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
                if (cognexLoader != null) cognexLoader.Abort();
                // Be sure to shudown the CogJobManager!!
                if (myJobManager != null) myJobManager.Shutdown();
            }
            catch (Exception ex)
            {
                Trace.WriteLine("Vision-Pro shutdown. Excepiton:" + ex.Message);
            }
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
                    this.visionProFilePath = VisionProFilePath;
                    cognexLoader = new Thread(loadCognex);
                    cognexLoader.Start();
                    return;
                }
            }
            Trace.WriteLine("No VisionProFile selected.");
        }

        private void loadCognex()
        {
            try
            {
                myJobManager = (CogJobManager)CogSerializer.LoadObjectFromFile(visionProFilePath);
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

        private void saveCognexFile()
        {
            try
            {
                if (Convert.ToBoolean(settings.CreateOrGetValue("FileDialog", "CreateBackup", "True"))) 
                {
                    String BackupFileName = visionProFilePath + DateTime.Now.ToString("_yyyyMMdd_HHmmss");
                    Trace.WriteLine("Creating backup: " + BackupFileName);
                    System.IO.File.Move(visionProFilePath, BackupFileName);
                }
                CogSerializer.SaveObjectToFile(myJobManager, visionProFilePath);
                Trace.WriteLine("File saved: " + visionProFilePath);
            }
            catch (Exception ex)
            {
                Trace.WriteLine("Error saving file. Exception: " + ex.Message);
            }
        }

        private void setRelease(CogToolGroup toolGroup)
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
                    setRelease(tmpToolGroup);
                }
                else
                {
                    //Trace.WriteLine(" Tool: " + tool.Name);
                }
            }
        }

        private void modifyToolsInToolgroup(CogToolGroup toolGroup, ModificationMode mode)
        {
            Trace.WriteLine("ModifyToolsInToolGroup: Mode="+ mode);
            foreach (ICogTool tool in toolGroup.Tools)
            {
                //CogInputImageTool (Nicht beachten, da Kamera)

                //CogToolGroup
                if ((tmpToolGroup = (tool as CogToolGroup)) != null)
                {
                    tmpProcessingString = "Processing Group: " + tool.Name;
                    //recursively check all ToolBlocks/ToolGroups
                    modifyToolsInToolgroup(tmpToolGroup, mode);
                }
                else
                {
                    tmpProcessingString = "-> Processing: " + tool.Name;

                    //CogBlobTool
                    if (tool as Cognex.VisionPro.Blob.CogBlobTool != null)
                    {
                        Cognex.VisionPro.Blob.CogBlobTool Blob = tool as Cognex.VisionPro.Blob.CogBlobTool;

                        switch (mode)
                        {
                            case ModificationMode.disable:
                                Blob.CurrentRecordEnable = Cognex.VisionPro.Blob.CogBlobCurrentRecordConstants.None;
                                Blob.LastRunRecordDiagEnable = Cognex.VisionPro.Blob.CogBlobLastRunRecordDiagConstants.None;
                                Blob.LastRunRecordEnable = Cognex.VisionPro.Blob.CogBlobLastRunRecordConstants.None;
                                break;
                            
                            case ModificationMode.enableALL:
                                Blob.CurrentRecordEnable = Cognex.VisionPro.Blob.CogBlobCurrentRecordConstants.All;
                                Blob.LastRunRecordDiagEnable = Cognex.VisionPro.Blob.CogBlobLastRunRecordDiagConstants.All;
                                Blob.LastRunRecordEnable = Cognex.VisionPro.Blob.CogBlobLastRunRecordConstants.All;
                                break;
                            
                            case ModificationMode.standard:
                                //Blob.CurrentRecordEnable = Region | InputImageMask | Histogram | InputImage
                                Blob.CurrentRecordEnable
                                    = Cognex.VisionPro.Blob.CogBlobCurrentRecordConstants.Region
                                    | Cognex.VisionPro.Blob.CogBlobCurrentRecordConstants.InputImageMask
                                    | Cognex.VisionPro.Blob.CogBlobCurrentRecordConstants.Histogram
                                    | Cognex.VisionPro.Blob.CogBlobCurrentRecordConstants.InputImage
                                    ;

                                //Blob.LastRunRecordDiagEnable = InputImageByReference
                                Blob.LastRunRecordDiagEnable
                                    = Cognex.VisionPro.Blob.CogBlobLastRunRecordDiagConstants.InputImageByReference
                                    ;

                                //Blob.LastRunRecordEnable = ResultsBoundary | BlobImageUnfiltered | BlobImage
                                Blob.LastRunRecordEnable
                                    = Cognex.VisionPro.Blob.CogBlobLastRunRecordConstants.ResultsBoundary
                                    | Cognex.VisionPro.Blob.CogBlobLastRunRecordConstants.BlobImageUnfiltered
                                    | Cognex.VisionPro.Blob.CogBlobLastRunRecordConstants.BlobImage
                                    ;
                                break;
                        }
                        
                    }

                    //CogPMAlignTool
                    else if (tool as Cognex.VisionPro.PMAlign.CogPMAlignTool != null)
                    {
                        Cognex.VisionPro.PMAlign.CogPMAlignTool PMAlign = tool as Cognex.VisionPro.PMAlign.CogPMAlignTool;

                        switch (mode)
                        {
                            case ModificationMode.disable:
                                PMAlign.CurrentRecordEnable = Cognex.VisionPro.PMAlign.CogPMAlignCurrentRecordConstants.None;
                                PMAlign.LastRunRecordDiagEnable = Cognex.VisionPro.PMAlign.CogPMAlignLastRunRecordDiagConstants.None;
                                PMAlign.LastRunRecordEnable = Cognex.VisionPro.PMAlign.CogPMAlignLastRunRecordConstants.None;
                                break;

                            case ModificationMode.enableALL:
                                PMAlign.CurrentRecordEnable = Cognex.VisionPro.PMAlign.CogPMAlignCurrentRecordConstants.All;
                                PMAlign.LastRunRecordDiagEnable = Cognex.VisionPro.PMAlign.CogPMAlignLastRunRecordDiagConstants.All;
                                PMAlign.LastRunRecordEnable = Cognex.VisionPro.PMAlign.CogPMAlignLastRunRecordConstants.All;
                                break;

                            case ModificationMode.standard:
                                //PMAlign.CurrentRecordEnable = InputImage | PatternOrigin | SearchImageMask | SearchRegion | TrainShapeModels | TrainImageMask | TrainRegion | TrainImage
                                PMAlign.CurrentRecordEnable
                                    = Cognex.VisionPro.PMAlign.CogPMAlignCurrentRecordConstants.InputImage
                                    | Cognex.VisionPro.PMAlign.CogPMAlignCurrentRecordConstants.PatternOrigin
                                    | Cognex.VisionPro.PMAlign.CogPMAlignCurrentRecordConstants.SearchImageMask
                                    | Cognex.VisionPro.PMAlign.CogPMAlignCurrentRecordConstants.SearchRegion
                                    | Cognex.VisionPro.PMAlign.CogPMAlignCurrentRecordConstants.TrainShapeModels
                                    | Cognex.VisionPro.PMAlign.CogPMAlignCurrentRecordConstants.TrainImageMask
                                    | Cognex.VisionPro.PMAlign.CogPMAlignCurrentRecordConstants.TrainRegion
                                    | Cognex.VisionPro.PMAlign.CogPMAlignCurrentRecordConstants.TrainImage
                                    ;

                                //PMAlign.LastRunRecordDiagEnable = InputImageByReference
                                PMAlign.LastRunRecordDiagEnable
                                    = Cognex.VisionPro.PMAlign.CogPMAlignLastRunRecordDiagConstants.InputImageByReference
                                    ;

                                //PMAlign.LastRunRecordEnable = ResultsMatchRegion | ResultsOrigin
                                PMAlign.LastRunRecordEnable
                                    = Cognex.VisionPro.PMAlign.CogPMAlignLastRunRecordConstants.ResultsMatchRegion
                                    | Cognex.VisionPro.PMAlign.CogPMAlignLastRunRecordConstants.ResultsOrigin
                                    ;
                                break;
                        }

                    }

                    //CogSearchMaxTool
                    else if (tool as Cognex.VisionPro.SearchMax.CogSearchMaxTool != null)
                    {
                        Cognex.VisionPro.SearchMax.CogSearchMaxTool SearchMax = tool as Cognex.VisionPro.SearchMax.CogSearchMaxTool;

                        switch (mode)
                        {
                            case ModificationMode.disable:
                                SearchMax.CurrentRecordEnable = Cognex.VisionPro.SearchMax.CogSearchMaxCurrentRecordConstants.None;
                                SearchMax.LastRunRecordDiagEnable = Cognex.VisionPro.SearchMax.CogSearchMaxLastRunRecordDiagConstants.None;
                                SearchMax.LastRunRecordEnable = Cognex.VisionPro.SearchMax.CogSearchMaxLastRunRecordConstants.None;
                                break;

                            case ModificationMode.enableALL:
                                SearchMax.CurrentRecordEnable = Cognex.VisionPro.SearchMax.CogSearchMaxCurrentRecordConstants.All;
                                SearchMax.LastRunRecordDiagEnable = Cognex.VisionPro.SearchMax.CogSearchMaxLastRunRecordDiagConstants.All;
                                SearchMax.LastRunRecordEnable = Cognex.VisionPro.SearchMax.CogSearchMaxLastRunRecordConstants.All;
                                break;

                            case ModificationMode.standard:
                                //SearchMax.CurrentRecordEnable = PatternOrigin | TrainRegion | TrainImageMask | TrainImage | SearchImageMask | SearchRegion | InputImage
                                SearchMax.CurrentRecordEnable
                                    = Cognex.VisionPro.SearchMax.CogSearchMaxCurrentRecordConstants.PatternOrigin
                                    | Cognex.VisionPro.SearchMax.CogSearchMaxCurrentRecordConstants.TrainRegion
                                    | Cognex.VisionPro.SearchMax.CogSearchMaxCurrentRecordConstants.TrainImageMask
                                    | Cognex.VisionPro.SearchMax.CogSearchMaxCurrentRecordConstants.TrainImage
                                    | Cognex.VisionPro.SearchMax.CogSearchMaxCurrentRecordConstants.SearchImageMask
                                    | Cognex.VisionPro.SearchMax.CogSearchMaxCurrentRecordConstants.SearchRegion
                                    | Cognex.VisionPro.SearchMax.CogSearchMaxCurrentRecordConstants.InputImage
                                    ;

                                //SearchMax.LastRunRecordDiagEnable = InputImageByReference
                                SearchMax.LastRunRecordDiagEnable
                                    = Cognex.VisionPro.SearchMax.CogSearchMaxLastRunRecordDiagConstants.InputImageByReference
                                    ;

                                //SearchMax.LastRunRecordEnable = ResultsOrigin | ResultsMatchRegion
                                SearchMax.LastRunRecordEnable
                                    = Cognex.VisionPro.SearchMax.CogSearchMaxLastRunRecordConstants.ResultsOrigin
                                    | Cognex.VisionPro.SearchMax.CogSearchMaxLastRunRecordConstants.ResultsMatchRegion
                                    ;
                                break;
                        }
                        
                    }

                    //CogFixtureTool
                    else if (tool as Cognex.VisionPro.CalibFix.CogFixtureTool != null)
                    {
                        Cognex.VisionPro.CalibFix.CogFixtureTool FixtureTool = tool as Cognex.VisionPro.CalibFix.CogFixtureTool;

                        switch (mode)
                        {
                            case ModificationMode.disable:
                                FixtureTool.CurrentRecordEnable = Cognex.VisionPro.CalibFix.CogFixtureCurrentRecordConstants.None;
                                FixtureTool.LastRunRecordDiagEnable = Cognex.VisionPro.CalibFix.CogFixtureLastRunRecordDiagConstants.None;
                                FixtureTool.LastRunRecordEnable = Cognex.VisionPro.CalibFix.CogFixtureLastRunRecordConstants.None;
                                break;

                            case ModificationMode.enableALL:
                                FixtureTool.CurrentRecordEnable = Cognex.VisionPro.CalibFix.CogFixtureCurrentRecordConstants.All;
                                FixtureTool.LastRunRecordDiagEnable = Cognex.VisionPro.CalibFix.CogFixtureLastRunRecordDiagConstants.All;
                                FixtureTool.LastRunRecordEnable = Cognex.VisionPro.CalibFix.CogFixtureLastRunRecordConstants.All;
                                break;

                            case ModificationMode.standard:
                                //FixtureTool.CurrentRecordEnable = InputImage | FixturedAxes
                                FixtureTool.CurrentRecordEnable
                                    = Cognex.VisionPro.CalibFix.CogFixtureCurrentRecordConstants.InputImage
                                    | Cognex.VisionPro.CalibFix.CogFixtureCurrentRecordConstants.FixturedAxes
                                    ;

                                //FixtureTool.LastRunRecordDiagEnable = FixturedAxes
                                FixtureTool.LastRunRecordDiagEnable
                                    = Cognex.VisionPro.CalibFix.CogFixtureLastRunRecordDiagConstants.FixturedAxes
                                    ;

                                //FixtureTool.LastRunRecordEnable = OutputImage
                                FixtureTool.LastRunRecordEnable
                                    = Cognex.VisionPro.CalibFix.CogFixtureLastRunRecordConstants.OutputImage
                                    ;
                                break;
                        }
                        
                    }

                    //CogHistogramTool
                    else if (tool as Cognex.VisionPro.ImageProcessing.CogHistogramTool != null)
                    {
                        Cognex.VisionPro.ImageProcessing.CogHistogramTool Histogram = tool as Cognex.VisionPro.ImageProcessing.CogHistogramTool;

                        switch (mode)
                        {
                            case ModificationMode.disable:
                                Histogram.CurrentRecordEnable = Cognex.VisionPro.ImageProcessing.CogHistogramCurrentRecordConstants.None;
                                Histogram.LastRunRecordDiagEnable = Cognex.VisionPro.ImageProcessing.CogHistogramLastRunRecordDiagConstants.None;
                                Histogram.LastRunRecordEnable = Cognex.VisionPro.ImageProcessing.CogHistogramLastRunRecordConstants.None;
                                break;

                            case ModificationMode.enableALL:
                                Histogram.CurrentRecordEnable = Cognex.VisionPro.ImageProcessing.CogHistogramCurrentRecordConstants.All;
                                Histogram.LastRunRecordDiagEnable = Cognex.VisionPro.ImageProcessing.CogHistogramLastRunRecordDiagConstants.All;
                                Histogram.LastRunRecordEnable = Cognex.VisionPro.ImageProcessing.CogHistogramLastRunRecordConstants.All;
                                break;

                            case ModificationMode.standard:
                                //Histogram.CurrentRecordEnable = Region | InputImageMask | InputImage
                                Histogram.CurrentRecordEnable
                                    = Cognex.VisionPro.ImageProcessing.CogHistogramCurrentRecordConstants.Region
                                    | Cognex.VisionPro.ImageProcessing.CogHistogramCurrentRecordConstants.InputImageMask
                                    | Cognex.VisionPro.ImageProcessing.CogHistogramCurrentRecordConstants.InputImage
                                    ;

                                //Histogram.LastRunRecordDiagEnable = InputImageByReference
                                Histogram.LastRunRecordDiagEnable
                                    = Cognex.VisionPro.ImageProcessing.CogHistogramLastRunRecordDiagConstants.InputImageByReference
                                    ;

                                //Histogram.LastRunRecordEnable = Mean | Histogram
                                Histogram.LastRunRecordEnable
                                    = Cognex.VisionPro.ImageProcessing.CogHistogramLastRunRecordConstants.Mean
                                    | Cognex.VisionPro.ImageProcessing.CogHistogramLastRunRecordConstants.Histogram
                                    ;
                                break;
                        }
                        
                    }

                    //CogFindLineTool
                    else if (tool as Cognex.VisionPro.Caliper.CogFindLineTool != null)
                    {
                        Cognex.VisionPro.Caliper.CogFindLineTool FindLine = tool as Cognex.VisionPro.Caliper.CogFindLineTool;

                        switch (mode)
                        {
                            case ModificationMode.disable:
                                FindLine.CurrentRecordEnable = Cognex.VisionPro.Caliper.CogFindLineCurrentRecordConstants.None;
                                FindLine.LastRunRecordDiagEnable = Cognex.VisionPro.Caliper.CogFindLineLastRunRecordDiagConstants.None;
                                FindLine.LastRunRecordEnable = Cognex.VisionPro.Caliper.CogFindLineLastRunRecordConstants.None;
                                break;

                            case ModificationMode.enableALL:
                                FindLine.CurrentRecordEnable = Cognex.VisionPro.Caliper.CogFindLineCurrentRecordConstants.All;
                                FindLine.LastRunRecordDiagEnable = Cognex.VisionPro.Caliper.CogFindLineLastRunRecordDiagConstants.All;
                                FindLine.LastRunRecordEnable = Cognex.VisionPro.Caliper.CogFindLineLastRunRecordConstants.All;
                                break;

                            case ModificationMode.standard:
                                //FindLine.CurrentRecordEnable = ExpectedLineSegment | InteractiveCaliperSearchDirection | InteractiveCaliperSize | CaliperRegions | InputImage
                                FindLine.CurrentRecordEnable
                                    = Cognex.VisionPro.Caliper.CogFindLineCurrentRecordConstants.ExpectedLineSegment
                                    | Cognex.VisionPro.Caliper.CogFindLineCurrentRecordConstants.InteractiveCaliperSearchDirection
                                    | Cognex.VisionPro.Caliper.CogFindLineCurrentRecordConstants.InteractiveCaliperSize
                                    | Cognex.VisionPro.Caliper.CogFindLineCurrentRecordConstants.CaliperRegions
                                    | Cognex.VisionPro.Caliper.CogFindLineCurrentRecordConstants.InputImage
                                    ;

                                //FindLine.LastRunRecordDiagEnable = InputImageByReference
                                FindLine.LastRunRecordDiagEnable
                                    = Cognex.VisionPro.Caliper.CogFindLineLastRunRecordDiagConstants.InputImageByReference
                                    ;

                                //FindLine.LastRunRecordEnable = ResultsIgnoredPoints | ResultsUsedPoints | BestFitLineSegment
                                FindLine.LastRunRecordEnable
                                    = Cognex.VisionPro.Caliper.CogFindLineLastRunRecordConstants.ResultsIgnoredPoints
                                    | Cognex.VisionPro.Caliper.CogFindLineLastRunRecordConstants.ResultsUsedPoints
                                    | Cognex.VisionPro.Caliper.CogFindLineLastRunRecordConstants.BestFitLineSegment
                                    ;
                                break;
                        }
                        
                    }

                    //CogIPOneImageTool
                    else if (tool as Cognex.VisionPro.ImageProcessing.CogIPOneImageTool != null)
                    {
                        Cognex.VisionPro.ImageProcessing.CogIPOneImageTool oneImageTool = tool as Cognex.VisionPro.ImageProcessing.CogIPOneImageTool;

                        switch (mode)
                        {
                            case ModificationMode.disable:
                                oneImageTool.CurrentRecordEnable = Cognex.VisionPro.ImageProcessing.CogIPOneImageCurrentRecordConstants.None;
                                oneImageTool.LastRunRecordDiagEnable = Cognex.VisionPro.ImageProcessing.CogIPOneImageLastRunRecordDiagConstants.None;
                                oneImageTool.LastRunRecordEnable = Cognex.VisionPro.ImageProcessing.CogIPOneImageLastRunRecordConstants.None;
                                break;

                            case ModificationMode.enableALL:
                                oneImageTool.CurrentRecordEnable = Cognex.VisionPro.ImageProcessing.CogIPOneImageCurrentRecordConstants.All;
                                oneImageTool.LastRunRecordDiagEnable = Cognex.VisionPro.ImageProcessing.CogIPOneImageLastRunRecordDiagConstants.All;
                                oneImageTool.LastRunRecordEnable = Cognex.VisionPro.ImageProcessing.CogIPOneImageLastRunRecordConstants.All;
                                break;

                            case ModificationMode.standard:
                                //oneImageTool.CurrentRecordEnable = Region | InputImage
                                oneImageTool.CurrentRecordEnable
                                    = Cognex.VisionPro.ImageProcessing.CogIPOneImageCurrentRecordConstants.Region
                                    | Cognex.VisionPro.ImageProcessing.CogIPOneImageCurrentRecordConstants.InputImage
                                    ;

                                //oneImageTool.LastRunRecordDiagEnable = InputImageByReference
                                oneImageTool.LastRunRecordDiagEnable
                                    = Cognex.VisionPro.ImageProcessing.CogIPOneImageLastRunRecordDiagConstants.InputImageByReference
                                    ;

                                //oneImageTool.LastRunRecordEnable = OutputImage
                                oneImageTool.LastRunRecordEnable
                                    = Cognex.VisionPro.ImageProcessing.CogIPOneImageLastRunRecordConstants.OutputImage
                                    ;
                                break;
                        }
                    }

                    ////Handle further tools here
                    //CogPixelMapTool
                    //CogImageConvertTool


                    ////Cases for modes
                    //switch (mode)
                    //{
                    //    case ModificationMode.disable:

                    //        break;
                        
                    //    case ModificationMode.enableALL:

                    //        break;

                    //    case ModificationMode.standard:

                    //        break;
                    //}
                    
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

        #region Button Clicks
        private void btn_openQuickBuild_Click(object sender, RoutedEventArgs e)
        {
            Trace.WriteLine("Open current project in QuickBuild ...");
            saveCognexFile();

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

            setRelease(myJob.VisionTool as CogToolGroup);
            saveCognexFile();
        }

        private void btn_disableImages_Click(object sender, RoutedEventArgs e)
        {
            Trace.WriteLine("Disable all additional images...");
            if (myJob == null) return;

            modifyToolsInToolgroup(myJob.VisionTool as CogToolGroup, ModificationMode.disable);
            saveCognexFile();
        }

        private void btn_enableImages_Click(object sender, RoutedEventArgs e)
        {
            Trace.WriteLine("Enable all additional images...");
            if (myJob == null) return;

            modifyToolsInToolgroup(myJob.VisionTool as CogToolGroup, ModificationMode.enableALL);
            saveCognexFile();
        }

        private void btn_standardImages_Click(object sender, RoutedEventArgs e)
        {
            Trace.WriteLine("Set standard tool-images...");
            if (myJob == null) return;

            modifyToolsInToolgroup(myJob.VisionTool as CogToolGroup, ModificationMode.standard);
            saveCognexFile();
        }

        #endregion
    }
}
