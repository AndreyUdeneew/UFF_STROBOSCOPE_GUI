﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using SpinnakerNET;
using SpinnakerNET.GUI;
using SpinnakerNET.GUI.WPFControls;
using SpinnakerNET.GenApi;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Threading;
using Color = System.Drawing.Color;
using System.IO;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using GxIAPINET;
using System.IO.Ports;
using System.ComponentModel;
using System.Windows.Forms;
using System.Windows;
using System.Windows.Controls;
using RadioButton = System.Windows.Controls.RadioButton;
using MessageBox = System.Windows.MessageBox;
using Steema.TeeChart;
using System.Windows.Media.Animation;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using Window = System.Windows.Window;
using CefSharp.DevTools.Network;
using CefSharp;


namespace SimplestSpinWPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        /// <summary>
        /// Init flags
        /// </summary>
        static bool InitFlir = false;
        static bool InitDAO = true;
        int fontSize = 50;

        private bool _isReading;
        private WriteableBitmap _heatmapBitmap;
        private const int ThermoWidth = 32; // MLX90640 sensor width
        private const int ThermoHeight = 24; // MLX90640 sensor height
        (byte r, byte g, byte b)[] Rainbow = new (byte r, byte g, byte b)[360];



        IManagedCamera SpinCamColor = null;
        //PropertyGridControl gridControl = new PropertyGridControl();
        byte[] DivideCache = new byte[256 * 256];
        byte[] HSVToRGBCache = new byte[256 * 4];
        int FLiRCamCount = 0, DAOCamCount = 0;

        Thread RefreshThread;
        //Thread ModeSelectThread;
        public MainWindow()
        {
            InitializeComponent();
            _isReading = false;

            // Initialize the WriteableBitmap for the heatmap
            _heatmapBitmap = new WriteableBitmap(ThermoWidth, ThermoHeight, 96, 96, PixelFormats.Bgr32, null);
            HeatmapImage.Source = _heatmapBitmap;
            //RadioButton rb = new RadioButton { IsChecked = true, GroupName = "Languages", Content = "JavaScript" };
            //rb.Checked += RadioButton_Checked;
            //stackPanel.Children.Add(rb);

            for (int i = 0; i < 256; i++)
                for (int j = 0; j < 256; j++)
                    if (j == 0)
                        DivideCache[i * 256 + j] = 0;
                    else
                        DivideCache[i * 256 + j] = (byte)(i * additionalCoef / j);

            for (int i = 0, j = 0; i < 255; i++)
            {
                int r, g, b;
                HsvToRgb((255 - i) * 0.8, 0.9, 0.9, out r, out g, out b);
                HSVToRGBCache[j++] = (byte)b;
                HSVToRGBCache[j++] = (byte)g;
                HSVToRGBCache[j++] = (byte)r;
                HSVToRGBCache[j++] = 0;
            }

            //LayoutLeft.Children.Add(gridControl);

            if (InitFlir)
            {
                fontSize = 50;
                FlirCamInit();
                DrawDiffCheckBox.IsChecked = false;
            }

            if (InitDAO)
            {
                fontSize = 5000;
                DAOCamInit();
                DrawDiffCheckBox.IsChecked = true;
            }


            if (FLiRCamCount < 1 && DAOCamCount < 1)
            {
                System.Windows.MessageBox.Show("No FLIR camera is found!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                this.Close();
                return;
            }


            RefreshThread = new Thread(GetImages);
            //ModeSelectThread = new Thread(ModeSelection);
            RefreshThread.Start();
            //ModeSelectThread.Start();
            GraphGrid.Visibility = System.Windows.Visibility.Hidden;
            InitPlot();
        }

        const int PortSpeed = 115200;
        SerialPort p = null;
        bool Refreshing = false;
        int i = 0;
        int additionalCoef = 4;
        long LastImageSum = 0;
        public BitmapSource convertedImage = null;
        public BitmapSource PrevConvertedImage = null;
        public BitmapSource redImage = null;
        public BitmapSource greenImage = null;
        public BitmapSource background = null;
        public BitmapSource UV = null;
        public BitmapSource R2G = null;
        public BitmapSource heatmapR2G = null;
        public BitmapSource heatmapRed = null;
        public BitmapSource heatmapGreen = null;
        public BitmapSource PSEUDO = null;
        public BitmapSource HeatMap = null;
        public BitmapSource bsOut = null;
        int FIcounter = 0;
        int averageLimit = 10;
        int framesCounter = 0;
        string savingMode = "";
        int nFramesBeforeSaving = 100;
        int checkNpixelsInCursor = 0;
        int tSum = 0;
        double tSred = 0;
        int isPlot = 0;

        string _portName = "";
        volatile string CMD = "";
        bool Oxy = false;
        string AIM_color = "blue";

        double FI_norma = 1;
        double FI = 0;
        int deltaSum = 0;
        double sumSred = 0;
        string FI_string = "";

        double FIR_MAX = 0;
        double FIR_norma = 1;
        double FIR = 0;
        double FIR_Real = 0;
        string FIR_string = "";
        string FIR_MAX_string = "";

        double FIV_MAX = 0;
        double FIV_norma = 1;
        double FIV = 0;
        double FIV_Real = 0;
        string FIV_string = "";
        string FIV_MAX_string = "";

        double FIG_MAX = 0;
        double FIG_norma = 1;
        double FIG = 0;
        double FIG_Real = 0;
        string FIG_string = "";
        string FIG_MAX_string = "";

        string fileName4Saving = "";
        string fileNameDecreased = "";
        string temperature = "";

        double bleaching_red = 0;
        double bleaching_viol = 0;
        double bleaching_green = 0;
        string bleaching_viol_string = "";
        string bleaching_red_string = "";
        string bleaching_green_string = "";

        string TimerValueString = "";
        string TimerValueStartString = "";
        int startMin = 0;
        int startSec = 0;
        int curMin = 0;
        int curSec = 0;

        int ampRed = 0;
        int ampGreen = 0;
        int ampR2G = 0;
        int ampR_G = 0;
        int ampRLED = 0;
        int ampGLED = 0;
        int ampICG = 0;
        int ampOxy = 0;
        int ampBOTH = 0;
        int ampCur = 0;

        int desiredBleaching = 0;
        int bleachingMeas = 0;
        int startFrames = 0;
        string response = "";

        Stopwatch d = Stopwatch.StartNew();

        bool SeqEnabled = false;
        //bool OxyAlter = (bool)CheckBoxAutofocus.IsChecked;
        string isSerial = "";

        public long PrevImageSum = 0;


        static IGXFactory CamDriver = null;                      ///< The handle for factory
        IGXDevice Camera = null;                        ///< The handle for device
        IGXStream CamStream = null;                        ///< The handle for stream
        IGXFeatureControl FeatureControl = null;        ///< The handle for feature control
        IGXFeatureControl StreamFeatureControl = null;  ///< The object for stream feature control
        WriteableBitmap CurIm = null;
        DispatcherTimer Timer = new DispatcherTimer();
        int FPSFrameCounter = 0;
        static List<IGXDeviceInfo> CamList = null;
        TChart chart = new TChart();

        void InitPlot()
        {
            WinFormsHost.Child = chart;
            Steema.TeeChart.Styles.Line lineSeries = new Steema.TeeChart.Styles.Line();
            lineSeries.Title = "Central cursor";
            lineSeries.FillSampleValues(); // Optional: Generate sample data for the series
            chart.Series.Add(lineSeries);
            chart.Header.Visible = false;
            chart.Axes.Bottom.Title.Text = "Millisecond";
            chart.Axes.Left.Title.Text = "A.U.";
            chart.Legend.Visible = false;
            chart.Panel.Transparency = 100;
            chart.Aspect.Chart3DPercent = 0;
            chart.Aspect.Width3D = 0; chart.Aspect.View3D = false;
            lineSeries.LinePen.Width = 3;
            chart.Axes.Bottom.AxisPen.Color = Color.White;
            chart.Axes.Left.AxisPen.Color = Color.White;
        }



        void DAOCamInit()
        {
            int n = 0; ///cam number

            if (CamDriver == null)
            {
                try
                {
                    CamDriver = IGXFactory.GetInstance();
                    CamDriver.Init();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Error initializing Daheng driver: " + ex.Message);
                }
            }
            else
            {
                Debug.WriteLine("WARNINIG:no need to init Daheng");
            }

            if (CamList == null)
            {
                if (CamDriver == null)
                    return;
                try
                {
                    CamList = CamDriver.UpdateDeviceList(200).ToList();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Error enumerating DAHENG cams: " + ex.Message);
                }
                DAOCamCount = CamList.Count;
            }
            if (CamList == null)
            {
                Debug.WriteLine("Error enumerating DAHENG cams: ");
                return;
            }

            try
            {
                string SN = CamList[n].GetSN();
                Camera = CamDriver.OpenDeviceBySN(SN, GX_ACCESS_MODE.GX_ACCESS_EXCLUSIVE);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error initing DAHENG cam {0}, reason: {1}", n + 1, ex.Message);
                return;
            }

            if (null != Camera)
            {
                CamStream = Camera.OpenStream(0);
                StreamFeatureControl = CamStream.GetFeatureControl();
                FeatureControl = Camera.GetRemoteFeatureControl();
            }
            var FF = new List<string>();
            StreamFeatureControl.GetFeatureNameList(FF);

            if (FF.Contains("StreamBufferHandlingMode"))
                if (StreamFeatureControl.GetEnumFeature("StreamBufferHandlingMode").GetEnumEntryList().Contains("NewestOnly"))
                    StreamFeatureControl.GetEnumFeature("StreamBufferHandlingMode").SetValue("NewestOnly");



            CamStream.StartGrab();
            if (null != FeatureControl)
            {
                try
                {
                    FeatureControl.GetCommandFeature("AcquisitionStart").Execute();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("Error initing DAHENG cam {0} featues, reason: {1}", n + 1, ex.Message);
                    return;
                }
            }
            FPSFrameCounter = 0;

            try
            {
                CamStream.GetImage(1000000);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Error initing DAHENG cam {0}: cannot start reading, reason: {1}", n + 1, ex.Message);
                return;
            }
        }

        public BitmapSource DAOGetBitmap()
        {
            //WriteableBitmap outBitmap = null;
            BitmapSource outBitmapsource = null;

            string ErrorHappend = "";
            if (Camera == null || CamStream == null)
                ErrorHappend = "No DAO camera found";

            if (FPSFrameCounter % 100 == 0)
                GC.Collect();

            if (ErrorHappend == "")
                try
                {
                    Stopwatch sss = new Stopwatch();
                    sss.Start();
                    IImageData rawImage = CamStream.GetImageNoThrow(5000000);
                    int Width = (int)rawImage.GetWidth();
                    int Height = (int)rawImage.GetHeight();
                    sss.Restart();
                    if (rawImage.GetStatus() == GX_FRAME_STATUS_LIST.GX_FRAME_STATUS_SUCCESS)
                    {
                        IntPtr PureBGRFrame = rawImage.ConvertToRGB24(GX_VALID_BIT_LIST.GX_BIT_0_7, GX_BAYER_CONVERT_TYPE_LIST.GX_RAW2RGB_NEIGHBOUR, false);
                        //IntPtr PureBGRFrame = rawImage.ConvertToRGB
                        FPSFrameCounter++;
                        //outBitmap = new WriteableBitmap(BitmapSource.Create(Width, Height, 1, 1, PixelFormats.Bgr24, null, PureBGRFrame, Width * Height * 3, Width * 3));
                        outBitmapsource = BitmapSource.Create(Width, Height, 1, 1, PixelFormats.Rgb24, null, PureBGRFrame, Width * Height * 3, Width * 3);
                    }
                    else
                    {
                        return null;
                    }
                    //Debug.WriteLine("Convert{0} took {1}ms", FramesCount, sss.ElapsedMilliseconds);
                    sss.Restart();
                    rawImage.Destroy();
                    // Debug.WriteLine("Destroy{0} took {1}ms", FramesCount, sss.ElapsedMilliseconds);
                    sss.Stop();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                    ErrorHappend = "Failed to get the next image: " + ex.Message;

                }
            else ErrorHappend = "Camera is not streaming";

            if (ErrorHappend != "")
                Debug.WriteLine("Error reading DAO: " + ErrorHappend);

            //return outBitmap;
            return outBitmapsource;
        }

        void FlirCamInit()
        {
            //Camera search and initialization
            // Retrieve singleton reference to system object
            ManagedSystem system = new ManagedSystem();


            // Retrieve list of cameras from the system
            IList<IManagedCamera> cameraList = system.GetCameras();
            FLiRCamCount = cameraList.Count;
            if (FLiRCamCount < 1)
                return;

            var BlackFlys = cameraList.Where(c => c.GetTLDeviceNodeMap().GetNode<IString>("DeviceModelName").Value.Contains("Blackfly S")).ToArray();
            //var BlackFlys1 = cameraList.Where(c => c.GetTLDeviceNodeMap().Values.d.Contains("BlackFly S")).ToArray();
            //var Nodes = cameraList.Select(c => c.GetTLDeviceNodeMap()).ToArray();
            //if (BlackFlys.Length < 1)
            //    return;

            IManagedCamera cam = BlackFlys[0];

            if (cameraList.Count < 1)
            {
                MessageBox.Show("No FLIR camera is found!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                this.Close();
                Thread.CurrentThread.Abort();
                return;
            }

            //IManagedCamera cam = cameraList[0];
            SpinCamColor = cam;




            // Retrieve TL device nodemap and print device information
            INodeMap nodeMapTLDevice = cam.GetTLDeviceNodeMap();
            // Retrieve device serial number for filename
            String deviceSerialNumber = "";
            IString iDeviceSerialNumber = nodeMapTLDevice.GetNode<IString>("DeviceSerialNumber");
            deviceSerialNumber = iDeviceSerialNumber.Value;

            //Print device info
            ICategory category = nodeMapTLDevice.GetNode<ICategory>("DeviceInformation");
            for (int i = 0; i < category.Children.Length; i++)
            {
                Debug.WriteLine("{0}: {1}", category.Children[i].Name, (category.Children[i].IsReadable ? category.Children[i].ToString() : "Node not available"));
            }

            cam.Init();

            //cam.ExposureTimeMode.Value = cam.ExposureTimeMode.Symbolics[0];
            // Retrieve GenICam nodemap
            INodeMap nodeMap = cam.GetNodeMap();
            // Configure custom image settings
            //ConfigureCustomImageSettings(nodeMap);
            // Apply mono 8 pixel format
            IEnum iPixelFormat = nodeMap.GetNode<IEnum>("PixelFormat");
            IEnumEntry iPixelFormatMono8 = iPixelFormat.GetEntryByName("Mono8");
            //iPixelFormat.Value = iPixelFormatMono8.Value;
            // Apply minimum to offset X
            IInteger iOffsetX = nodeMap.GetNode<IInteger>("OffsetX");
            iOffsetX.Value = iOffsetX.Min;

            IEnum iAcquisitionMode = nodeMap.GetNode<IEnum>("AcquisitionMode");
            IEnumEntry iAcquisitionModeContinuous = iAcquisitionMode.GetEntryByName("Continuous");
            iAcquisitionMode.Value = iAcquisitionModeContinuous.Symbolic;
            // Begin acquiring images
            cam.BeginAcquisition();

            using (IManagedImage rawImage = SpinCamColor.GetNextImage())
            {
                if (!rawImage.IsIncomplete)
                {
                    using (IManagedImage RawConvertedImage = rawImage.Convert(PixelFormatEnums.BGRa8))
                    {
                        rawImage.ConvertToBitmapSource(PixelFormatEnums.BGR8, RawConvertedImage, ColorProcessingAlgorithm.DEFAULT);
                        convertedImage = RawConvertedImage.bitmapsource.Clone();
                        i++;
                        System.Windows.Application.Current.Dispatcher.Invoke(new Action(() => { RefreshScreen(); }), DispatcherPriority.Send);
                        this.Title = SpinCamColor.ExposureTime.ToString() + SpinCamColor.DeviceSerialNumber.ToString() + cam.DeviceModelName;
                    }
                }
            }

            cam.EndAcquisition();
            //gridControl.Connect(cam);
            //cam.DeInit();
            //system.Dispose();
        }
        private void GetImages()
        {
            if (FLiRCamCount > 0)
                for (; ; )
                    if (Refreshing)
                    {
                        try
                        {
                            using (IManagedImage rawImage = SpinCamColor.GetNextImage())
                            {
                                if (!rawImage.IsIncomplete)
                                {
                                    try
                                    {
                                        CC.Dispatcher.Invoke(new Action(() =>
                                        {
                                            try
                                            {
                                                rawImage.ConvertToBitmapSource(PixelFormatEnums.BGR8, rawImage, ColorProcessingAlgorithm.DEFAULT);
                                                PrevConvertedImage = convertedImage;
                                                convertedImage = rawImage.bitmapsource;
                                                i++;
                                                PrevImageSum = LastImageSum;
                                                LastImageSum = FindSum(convertedImage);
                                                RefreshScreen();
                                            }
                                            catch { }
                                            ;
                                        }), DispatcherPriority.Normal);
                                    }
                                    catch (Exception ex)
                                    {
                                        Debug.WriteLine(i.ToString() + " " + ex.Message + "\n" + ex.StackTrace.ToString());
                                    }
                                }
                                if (i % 200 == 0)
                                    GC.Collect();
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine("GetFrameError: " + ex.Message);
                        }
                    }
                    else { }

            else if (DAOCamCount > 0)
                for (; ; )
                    if (Refreshing)
                    {
                        Thread.Sleep(1);
                        try
                        {

                            CC.Dispatcher.Invoke(new Action(() =>
                            {
                                try
                                {
                                    var temp = DAOGetBitmap();
                                    PrevConvertedImage = convertedImage;
                                    //convertedImage = new WriteableBitmap(temp);
                                    convertedImage = temp;
                                    i++;
                                    PrevImageSum = LastImageSum;
                                    LastImageSum = FindSum(convertedImage);
                                    RefreshScreen();
                                }
                                catch (Exception ex) { Debug.WriteLine("Error output to screen: " + ex.Message); }
                                ;
                            }), DispatcherPriority.Normal);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine(i.ToString() + " " + ex.Message + "\n" + ex.StackTrace.ToString());
                        }
                    }
        }

        void RefreshScreen()
        {
            if (convertedImage == null)
                return;
            if (!(bool)DrawDiffCheckBox.IsChecked)
            {
                CC.Source = convertedImage;

            }
            else
            {
                CC.Source = FindColoredDifference(convertedImage, PrevConvertedImage, 0);
            }

            if(isPlot == 1)
            {
                TimerValueString = String.Format("{0}", d.Elapsed);
                Stopwatch_Label.Content = TimerValueString;
                //d.Restart();
                if (startFrames == 0)
                {

                }
                if (framesCounter == nFramesBeforeSaving/5)
                {
                    
                    GraphPoints.Add(new GraphPoint { sumSred = sumSred, millisecond = (DateTime.Now - ProgrammStarted).TotalMilliseconds });
                    if (TailKiller == null)
                    {
                        TailKiller = new System.Threading.Thread(() => { while (true) { CutGraphPointsTail(); Thread.Sleep(1000); } });
                        TailKiller.Start();
                        try { chart.Series[0].Clear(); } catch { }
                    }
                    framesCounter = 0;
                }
                framesCounter += 1;
            }

            //Debug.WriteLine(framesCounter.ToString());
            if (CheckBoxSeqEnabled.IsChecked == true)
            {
                if (startFrames == 0)
                {
                    TimerValueString = String.Format("{0}", d.Elapsed);
                    Stopwatch_Label.Content = TimerValueString;
                }


                if (savingMode == "seq")
                {
                    if (startFrames == 1)
                    {
                        if (framesCounter == 2)
                        {
                            radioButtonRed.IsChecked = true;
                        }
                        if (framesCounter == 6)
                        {
                            FIV_MAX = FIV;
                            this.SavingButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                        }
                        if (framesCounter == 20)
                        {
                            radioButtonRedLED.IsChecked = true;
                        }
                        if (framesCounter == 24)
                        {
                            FIR_MAX = FIR;
                            this.SavingButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                            //CMD = "M0";
                            //SendCMD();
                        }
                        if (framesCounter == 28)
                        {
                            radioButtonGreenLED.IsChecked = true;
                        }
                        if (framesCounter == 30)
                        {
                            FIG_MAX = FIG;
                            this.SavingButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                            //CMD = "M0";
                            //SendCMD();
                        }
                        if (framesCounter == 34)
                        {
                            radioButtonGreenLED.IsChecked = true;
                        }
                        if (framesCounter == 38)
                        {
                            this.SavingButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                        }
                        if (framesCounter == 40)
                        {
                            CMD = "M0";
                            SendCMD();
                        }
                        if (framesCounter == 42)
                        {
                            d.Restart();
                            startMin = 0;
                            startSec = 0;
                            radioButtonSeq.IsChecked = true;
                            TimerValueString = String.Format("{0}", d.Elapsed);
                            Stopwatch_Label.Content = TimerValueString;
                            startFrames = 0;
                            CMD = "T_ON";
                            SendCMD();
                        }
                    }
                    //DateTime d = DateTime.Now;
                    //curMin = d.Minute - startMin;
                    //curSec = d.Second - startSec;
                    //d.Stop();
                    framesCounter += 1;
                    if (framesCounter == nFramesBeforeSaving)
                    {
                        d.Stop();
                        CMD = "T_OFF";
                        SendCMD();
                    }
                    if (framesCounter == nFramesBeforeSaving + 2)
                    {
                        radioButtonRed.IsChecked = true;
                    }
                    if (framesCounter == nFramesBeforeSaving + 6)
                    {
                        this.SavingButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    }
                    if (framesCounter == nFramesBeforeSaving + 20)
                    {
                        radioButtonRedLED.IsChecked = true;
                    }
                    if (framesCounter == (nFramesBeforeSaving + 24))
                    {
                        this.SavingButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    }
                    if (framesCounter == nFramesBeforeSaving + 28)
                    {
                        radioButtonGreenLED.IsChecked = true;
                    }
                    if (framesCounter == (nFramesBeforeSaving + 32))
                    {
                        this.SavingButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                    }
                    if (framesCounter == (nFramesBeforeSaving + 36))
                    {
                        CMD = "M0";
                        SendCMD();
                    }
                    if (framesCounter == (nFramesBeforeSaving + 38))
                    {
                        framesCounter = 0;
                        //savingMode = "";
                        radioButtonSeq.IsChecked = true;
                        CMD = "T_ON";
                        d.Start();
                        //TimerValueString = String.Format("{0}", d.Elapsed);
                        SendCMD();
                    }
                }
            }
            Title = "STROBE II: Reads count:" + i.ToString() + " last sum:" + LastImageSum.ToString();
        }



        unsafe public long FindSum(BitmapSource bs)
        {
            if (bs == null)
                return -1;

            WriteableBitmap wb = new WriteableBitmap(bs);
            wb.Lock();
            byte* bb = (byte*)wb.BackBuffer.ToPointer();
            long Sum = 0;
            long L = (int)wb.PixelWidth * (int)wb.PixelHeight * 3;
            for (int i = 0; i < L; i += 3)
                //for (int x = 0, i = 0; x < wb.PixelWidth; x++)
                //    for (int y = 0; y < wb.PixelHeight; y++)
                Sum += bb[i];
            wb.Unlock();
            return Sum;
        }

        public int SendCMD()
        {
            //CommaCount = 0;
            //Buf = "";

            if (p != null)
                if (p.IsOpen)
                {
                    //string CMD2send = CMD;
                    //p.Write(CMD+"\n");
                    if (CMD == "M0")
                        p.Write("M0\n");
                    if (CMD == "M1")
                        p.Write("M1\n");
                    if (CMD == "M2")
                        p.Write("M2\n");
                    if (CMD == "M7")
                        p.Write("M7\n");
                    if (CMD == "M6")
                        p.Write("M6\n");
                    if (CMD == "M4")
                        p.Write("M4\n");
                    if (CMD == "M3")
                        p.Write("M3\n");
                    if (CMD == "M5")
                        p.Write("M5\n");
                    if (CMD == "T_ON")
                        p.Write("T_ON\n");
                    if (CMD == "T_OFF")
                        p.Write("T_OFF\n");
                    if (CMD == "AF")
                        p.Write("AF\n");
                    if (CMD == "AFON")
                        p.Write("AFON\n");
                    if (CMD == "AFOFF")
                        p.Write("AFOFF\n");
                    if (CMD == "TEPLON")
                        p.Write("TEPLON\n");
                    if (CMD == "TEPLOFF")
                        p.Write("TEPLOFF\n");
                    if (CMD == "TSHOT")
                        p.Write("TSHOT\n");
                    //if (CMD == "FC0")
                    //    p.Write("FC0\n");
                    //if (CMD == "FC1")
                    //    p.Write("FC1\n");
                    Debug.WriteLine(CMD);
                    CMD = "";
                }
            return 0;
        }

        public void filterChange(byte currentFilter)
        {


            if (currentFilter == 0)
            {
                CMD = "FC0";
                SendCMD();
                //if (p != null)
                //    if (p.IsOpen)
                //    {
                //        p.Write("FC0");
                //    }
            }

            else
            {
                CMD = "FC1";
                SendCMD();
                //if (p != null)
                //    if (p.IsOpen)
                //    {
                //        p.Write("FC1");
                //    }
            }
        }

        public void zoom(byte currentFilter)
        {


            if (currentFilter == 0)
            {
                CMD = "FC0";
                SendCMD();
            }

            else
            {
                CMD = "FC1";
                SendCMD();
            }

        }

        //public void sequentalSavingViol()
        //{

        //    this.SavingButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        //    savingMode = "red";

        //}

        //public void sequentalSavingRed()
        //{

        //    this.SavingButton.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
        //}

        private void RadioButtonR2G_Checked(object sender, EventArgs e)
        {
            // �������� ����������� � �������� ���� RadioButton
            RadioButton radioButtonR2G = (RadioButton)sender;
            if (radioButtonR2G.IsChecked == true)
            {
                //CMD = "T_OFF";
                //SendCMD();
                CMD = "M1";
                AIM_color = "blue";
                savingMode = "";
                SendCMD();
                //filterChange(0);
                //if (p != null)
                //    if (p.IsOpen)
                //    {
                //        p.Write("M1");
                //    }
            }
        }

        private void RadioButtonR_G_Checked(object sender, EventArgs e)
        {
            // �������� ����������� � �������� ���� RadioButton
            RadioButton radioButtonR_G = (RadioButton)sender;
            if (radioButtonR_G.IsChecked == true)
            {
                //CMD = "T_OFF";
                //SendCMD();
                AIM_color = "blue";
                filterChange(0);
                CMD = "M1";
                SendCMD();
                //if (p != null)
                //    if (p.IsOpen)
                //    {
                //        p.Write("M1");
                //    }
            }
        }

        private void RadioButtonGreen_Checked(object sender, EventArgs e)
        {
            // �������� ����������� � �������� ���� RadioButton
            RadioButton radioButtonGreen = (RadioButton)sender;
            if (radioButtonGreen.IsChecked == true)
            {
                //CMD = "T_OFF";
                //SendCMD();
                AIM_color = "blue";
                //filterChange(0);
                CMD = "M1";
                SendCMD();
                //if (p != null)
                //    if (p.IsOpen)
                //    {
                //        p.Write("M1");
                //    }
            }
        }

        private void RadioButtonRed_Checked(object sender, EventArgs e)
        {
            // �������� ����������� � �������� ���� RadioButton
            RadioButton radioButtonRed = (RadioButton)sender;
            if (radioButtonRed.IsChecked == true)
            {
                //CMD = "T_OFF";
                //SendCMD();
                AIM_color = "blue";
                //filterChange(0);
                CMD = "M1";
                SendCMD();
                //if (p != null)
                //    if (p.IsOpen)
                //    {
                //        p.Write("M1\r\n");
                //    }
            }
        }

        private void RadioButtonOxy_Checked(object sender, EventArgs e)
        {
            // �������� ����������� � �������� ���� RadioButton
            RadioButton radioButtonOxy = (RadioButton)sender;
            if (radioButtonOxy.IsChecked == true)
            {
                //CMD = "T_OFF";
                //SendCMD();
                AIM_color = "white";
                //filterChange(1);
                CMD = "M4";
                savingMode = "";
                SendCMD();
                //if (p != null)
                //    if (p.IsOpen)
                //    {
                //        p.Write("M4");
                //    }
                //Oxy = true;
            }
        }
        private void RadioButtonRedLED_Checked(object sender, RoutedEventArgs e)
        {
            // �������� ����������� � �������� ���� RadioButton
            RadioButton radioButtonRedLED = (RadioButton)sender;
            if (radioButtonRedLED.IsChecked == true)
            {
                //CMD = "T_OFF";
                //SendCMD();
                AIM_color = "red";
                //filterChange(0);
                CMD = "M2";
                SendCMD();
                //if (p != null)
                //    if (p.IsOpen)
                //    {
                //        p.Write("M2\r\n");
                //    }
            }
        }

        private void RadioButtonGreenLED_Checked(object sender, RoutedEventArgs e)
        {
            // �������� ����������� � �������� ���� RadioButton
            RadioButton radioButtonGreenLED = (RadioButton)sender;
            if (radioButtonGreenLED.IsChecked == true)
            {
                //CMD = "T_OFF";
                //SendCMD();
                AIM_color = "green";
                //filterChange(0);
                CMD = "M5";
                SendCMD();
                //if (p != null)
                //    if (p.IsOpen)
                //    {
                //        p.Write("M2\r\n");
                //    }
            }
        }

        private void RadioButtonBothLEDs_Checked(object sender, EventArgs e)
        {
            // �������� ����������� � �������� ���� RadioButton
            RadioButton radioButtonBothLEDs = (RadioButton)sender;
            if (radioButtonBothLEDs.IsChecked == true)
            {
                //CMD = "T_OFF";
                //SendCMD();
                AIM_color = "white";
                //filterChange(0);
                CMD = "M3";
                savingMode = "";
                SendCMD();
                //if (p != null)
                //    if (p.IsOpen)
                //    {
                //        p.Write("M3");
                //    }
            }
        }
        private void RadioButtonICG_Checked(object sender, EventArgs e)
        {
            //CMD = "T_OFF";
            //SendCMD();
            // �������� ����������� � �������� ���� RadioButton
            RadioButton radioButtonICG = (RadioButton)sender;
            if (radioButtonICG.IsChecked == true)
            {
                AIM_color = "blue";
                //filterChange(1);
                CMD = "M7";
                savingMode = "";
                SendCMD();
                //if (p != null)
                //    if (p.IsOpen)
                //    {
                //        p.Write("M7");
                //    }
            }
        }
        private void RadioButtonSeq_Checked(object sender, EventArgs e)
        {
            // �������� ����������� � �������� ���� RadioButton
            RadioButton radioButtonSeq = (RadioButton)sender;
            if (radioButtonSeq.IsChecked == true)
            {
                AIM_color = "blue";

                //CMD = "T_ON";
                //SendCMD();

                //DrawDiffCheckBox.IsChecked = false;
                //filterChange(0);
                //CMD = "M0";
                savingMode = "seq";
                //SendCMD();
                //if (p != null)
                //        if (p.IsOpen)
                //        {
                //            p.Write("M6");
                //        }
            }
        }

        private void RadioButtonNoLight_Checked(object sender, EventArgs e)
        {
            // �������� ����������� � �������� ���� RadioButton
            RadioButton radioNoLight = (RadioButton)sender;
            if (radioButtonNoLight.IsChecked == true)
            {
                AIM_color = "white";
                //filterChange(0);
                CMD = "M0";
                savingMode = "";
                SendCMD();
                //if (p != null)
                //        if (p.IsOpen)
                //        {
                //            p.Write("M6");
                //        }
            }
        }

        unsafe public WriteableBitmap FindColoredDifference(BitmapSource bs1, BitmapSource bs2, byte mode)
        {
            bool NoLight = (bool)radioButtonNoLight.IsChecked;
            bool GreenFlu = (bool)radioButtonGreen.IsChecked;
            bool RedFlu = (bool)radioButtonRed.IsChecked;
            bool R2G = (bool)radioButtonR2G.IsChecked;
            bool R_G = (bool)radioButtonR_G.IsChecked;
            bool Grayed = (bool)radioButtonGray.IsChecked;
            bool Pseudo = (bool)radioButtonHeatmap.IsChecked;
            bool Oxy = (bool)radioButtonOxy.IsChecked;
            bool RLED = (bool)radioButtonRedLED.IsChecked;
            bool GLED = (bool)radioButtonGreenLED.IsChecked;
            bool BOTH = (bool)radioButtonBothLEDs.IsChecked;
            bool ICG = (bool)radioButtonICG.IsChecked;
            bool Sequent = (bool)radioButtonSeq.IsChecked;

            bool OxyAlter = (bool)CheckBoxOxyAlter.IsChecked;




            if (mode == 1)
            {
                GreenFlu = true; RedFlu = false; R2G = false; R_G = false; Oxy = false; RLED = false; BOTH = false; ICG = false; Sequent = false; GLED = false;
            }
            if (mode == 2)
            {
                RedFlu = true; GreenFlu = false; R2G = false; R_G = false; Oxy = false; RLED = false; BOTH = false; ICG = false; Sequent = false; GLED = false;
            }
            if (mode == 3)
            {
                R2G = true; GreenFlu = false; RedFlu = false; R_G = false; Oxy = false; RLED = false; BOTH = false; ICG = false; Sequent = false; GLED = false;
            }
            if (mode == 5)
            {
                R_G = true; R2G = false; GreenFlu = false; RedFlu = false; Oxy = false; RLED = false; BOTH = false; ICG = false; Sequent = false; GLED = false;
            }
            if (mode == 6)
            {
                R_G = false; R2G = false; GreenFlu = false; RedFlu = false; Oxy = false; RLED = true; BOTH = false; ICG = false; Sequent = false; GLED = false;
            }
            if (mode == 7)
            {
                R_G = false; R2G = false; GreenFlu = false; RedFlu = false; Oxy = false; RLED = false; BOTH = true; ICG = false; Sequent = false; GLED = false;
            }
            if (mode == 8)
            {
                R_G = false; R2G = false; GreenFlu = false; RedFlu = false; Oxy = false; RLED = false; BOTH = false; ICG = true; Sequent = false; GLED = false;
            }
            if (mode == 9)
            {
                R_G = false; R2G = false; GreenFlu = false; RedFlu = false; Oxy = false; RLED = false; BOTH = false; ICG = false; Sequent = true; GLED = false;
            }
            if (mode == 10)
            {
                R_G = false; R2G = false; GreenFlu = false; RedFlu = false; Oxy = true; RLED = false; BOTH = false; ICG = false; Sequent = false; GLED = false;
            }
            if (mode == 11)
            {
                R_G = false; R2G = false; GreenFlu = false; RedFlu = false; Oxy = false; RLED = false; BOTH = false; ICG = false; Sequent = false; GLED = true;
            }
            if (mode == 4)
                Pseudo = true;

            if (bs1 == null)
                return null;

            WriteableBitmap wb1 = new WriteableBitmap(bs1);
            WriteableBitmap wb2 = new WriteableBitmap(bs2);
            WriteableBitmap wb;

            if (LastImageSum < PrevImageSum)
            {
                wb = new WriteableBitmap(bs1);
                background = wb1;
                UV = wb2;
            }
            else
            {
                wb = new WriteableBitmap(bs2);
                background = wb2;
                UV = wb1;
            }

            wb1.Lock(); wb2.Lock(); wb.Lock();
            byte* bb1 = (byte*)wb1.BackBuffer.ToPointer();
            byte* bb2 = (byte*)wb2.BackBuffer.ToPointer();
            byte* bb = (byte*)wb.BackBuffer.ToPointer();


            int dif = 0;
            double difDouble = 0;
            //double difRed = 0;
            //double difGreen = 0;
            int difRed = 0;
            int dataRed = 0;
            int dataIR = 0;
            int difGreen = 0;
            int OX = 0;
            int temp;
            byte res = 0;

            int ampCur = (int)(AmplificationSlider.Value);
            if (radioButtonRed.IsChecked == true)
            {
                if (CheckBoxSeqEnabled.IsChecked == true)
                {
                    ampCur = ampRed;
                    AmplificationSlider.Value = ampCur;
                }

            }
            if (radioButtonGreen.IsChecked == true)
            {
                if (CheckBoxSeqEnabled.IsChecked == true)
                {
                    ampCur = ampGreen;
                    AmplificationSlider.Value = ampCur;
                }
            }
            if (radioButtonR2G.IsChecked == true)
            {
                if (CheckBoxSeqEnabled.IsChecked == true)
                {
                    ampCur = ampR2G;
                    AmplificationSlider.Value = ampCur;
                }
            }
            if (radioButtonR_G.IsChecked == true)
            {
                if (CheckBoxSeqEnabled.IsChecked == true)
                {
                    ampCur = ampR_G;
                    AmplificationSlider.Value = ampCur;
                }
            }
            if (radioButtonRedLED.IsChecked == true)
            {
                if (CheckBoxSeqEnabled.IsChecked == true)
                {
                    ampCur = ampRLED;
                    AmplificationSlider.Value = ampCur;
                }
            }
            if (radioButtonICG.IsChecked == true)
            {
                if (CheckBoxSeqEnabled.IsChecked == true)
                {
                    ampCur = ampICG;
                    AmplificationSlider.Value = ampCur;
                }
                ;
            }
            if (radioButtonOxy.IsChecked == true)
            {
                if (CheckBoxSeqEnabled.IsChecked == true)
                {
                    ampCur = ampOxy;
                    AmplificationSlider.Value = ampCur;
                }
            }
            if (radioButtonBothLEDs.IsChecked == true)
            {
                if (CheckBoxSeqEnabled.IsChecked == true)
                {
                    ampCur = ampBOTH;
                    AmplificationSlider.Value = ampCur;
                }
            }
            if (radioButtonGreenLED.IsChecked == true)
            {
                if (CheckBoxSeqEnabled.IsChecked == true)
                {
                    ampCur = ampGLED;
                    AmplificationSlider.Value = ampCur;
                }
            }

            long L = (int)wb1.PixelWidth * (int)wb1.PixelHeight * 3;
            int width = (int)wb1.PixelWidth * 3;
            int height = (int)wb1.PixelHeight;
            int width2 = width * 2;
            int width3 = width * 3;
            int width4 = width * 4;
            int width5 = width * 5;
            int width6 = width * 6;
            int width7 = width * 7;
            int width8 = width * 8;
            int width9 = width * 9;
            //int cursorString = 0;

            double SummRed = 0;
            double SummGreen = 0;
            double SummFluor = 0;
            double SummWhite = 0;
            int w = (int)wb1.PixelWidth, h = (int)wb1.PixelHeight;
            int wCursor = 10;
            int wCursor3 = 30;
            int hCursor = 10;
            int firstCursorPixel;

            firstCursorPixel = (width * ((height / 2) - (wCursor / 2))) + (width / 2);
            FIcounter += 1;
            fixed (byte* DC = DivideCache, HSVC = HSVToRGBCache)
            {
                //deltaSum = 0;
                for (int b = 0, g = 1, r = 2, i = 0; b < L; b += 3, r += 3, g += 3, i++)
                {
                    if (GreenFlu)
                    {
                        dif = (bb1[g] - bb2[g]);
                        if (dif < 0)
                            dif = -dif;
                        //deltaSum += dif;
                    }

                    if (RedFlu)
                    {
                        dif = (bb1[r] - bb2[r]);
                        if (dif < 0)
                            dif = -dif;
                        //deltaSum += dif;
                    }

                    if (R2G)
                    {
                        difRed = bb1[r] - bb2[r];
                        if (difRed < 0)
                            difRed = -difRed;

                        difGreen = bb1[g] - bb2[g];
                        if (difGreen < 0)
                            difGreen = -difGreen;

                        if (difGreen == 0)
                        {
                            difGreen = 1;
                        }

                        dif = DC[(difRed << 8) + difGreen];
                        if (dif < 0)
                            dif = -dif;
                        //deltaSum += dif;
                    }

                    if (Oxy)
                    {
                        dataRed = bb1[r];
                        dataIR = bb2[r];

                        //if (OxyAlter)
                        //{
                        //    dataRed = bb2[r];
                        //    dataIR = bb1[r];
                        //}

                        if (dataIR == 0)
                        {
                            dataIR = 1;
                        }
                        if (dataRed == 0)
                        {
                            dataRed = 1;
                        }

                        if (dataRed < dataIR)
                        {
                            dif = DC[(dataIR << 8) + dataRed];
                        }
                        else
                        {
                            dif = DC[(dataRed << 8) + dataIR];
                        }
                    }

                    if (R_G)
                    {
                        difRed = bb1[r] - bb2[r];
                        if (difRed < 0)
                            difRed = -difRed;
                        difGreen = bb1[g] - bb2[g];
                        if (difGreen < 0)
                            difGreen = -difGreen;

                        dif = (difRed - difGreen);
                        if (dif < 0)
                            dif = 0;
                        //deltaSum += dif;
                    }

                    if (RLED)
                    {
                        dif = bb1[r] - bb2[r];
                        //dif = bb1[r] + bb1[r] + bb1[b] - bb2[r] - bb2[g] - bb2[b];
                        if (dif < 0)
                            dif = -dif;
                        //deltaSum += dif;
                    }

                    if (GLED)
                    {
                        dif = bb1[r] - bb2[r];
                        //dif = bb1[r] + bb1[r] + bb1[b] - bb2[r] - bb2[g] - bb2[b];
                        if (dif < 0)
                            dif = -dif;
                        //deltaSum += dif;
                    }

                    if (BOTH)
                    {
                        dif = bb1[r] - bb2[r];
                        if (dif < 0)
                            dif = -dif;
                        //deltaSum += dif;

                    }


                    if (ICG)
                    {
                        dif = bb1[r] - bb2[r];
                        //dif = bb1[r] + bb1[r] + bb1[b] - bb2[r] - bb2[g] - bb2[b];
                        if (dif < 0)
                            dif = -dif;

                    }

                    if (Sequent)
                    {
                        dif = bb1[r] - bb2[r];
                        if (dif < 0)
                            dif = -dif;

                    }

                    //if (dif < 0)
                    //    dif = -dif;
                    deltaSum += dif;

                    res = bb[g];
                    dif <<= ampCur;

                    //if (Oxy)
                    //{

                    //    temp = bb[g];
                    //}

                    if (!Oxy)
                    {
                        temp = res + dif;
                    }
                    else
                    {
                        //res = (byte)(dif);
                        temp = res + dif;

                        //temp = dif;
                    }

                    if (Pseudo)
                    {

                        //int j = dif << 2;
                        //int j = dif << 1;
                        //int j = dif;
                        //int j = (255 - temp) << 2;
                        int j = (255 - temp) << 3;
                        ;
                        //int j = (i % w) << 2;
                        bb[b] = HSVC[j++];
                        bb[g] = HSVC[j++];
                        bb[r] = HSVC[j++];
                    }
                    else
                    {

                        if (temp > 255)
                            bb[g] = 255;
                        else
                            bb[g] = (byte)(temp);
                    }

                    if (Grayed)
                    { bb[b] = res; bb[r] = res; }
                }
                
                //sumSred += deltaSum;
                
            }
            //firstCursorPixel = 0;
            for (int cursorString = 0; cursorString < hCursor; cursorString += 1)
                for (int b = ((firstCursorPixel + width * cursorString) + 0), g = ((firstCursorPixel + width * cursorString) + 1), r = ((firstCursorPixel + width * cursorString) + 2); b < ((firstCursorPixel + width * cursorString) + wCursor * 3); b += 3, r += 3, g += 3)
                {
                    //bb[g] >>= 1; bb[b] >>= 1; bb[r] >>= 1;
                    if (AIM_color == "red")
                    {
                        bb[g] = 0; bb[b] = 255; bb[r] = 0;
                    }
                    if (AIM_color == "white")
                    {
                        bb[g] = 255; bb[b] = 255; bb[r] = 255;
                    }
                    if (AIM_color == "blue")
                    {
                        bb[g] = 0; bb[b] = 0; bb[r] = 255;
                    }
                    if (AIM_color == "green")
                    {
                        bb[g] = 255; bb[b] = 0; bb[r] = 0;
                    }

                    if (background == wb1)
                    {
                        SummFluor += (bb2[r] - bb1[r]);
                        SummWhite += bb1[r];
                    }
                    else
                    {
                        SummFluor += (bb1[r] - bb2[r]);
                        SummWhite += bb2[r];
                    }
                }

            //if (FIcounter == averageLimit)
            //{
            //    FI = SummFluor / SummWhite;
            //    if (FI < 0)
            //        FI *= -1;
            //    FI = FI / FI_norma;
            //    FIcounter = 0;
            //    FI_textbox.Text = FI.ToString("F1");
            //}
            if (radioButtonSeq.IsChecked == false)
            {
                if (GreenFlu || RedFlu || R2G || R_G)
                {
                    FIV_Real = SummFluor / SummWhite;
                    FIV = FIV_Real / FIV_norma;
                    //deltaSum = FIV;
                    if (FIV > FIV_MAX)
                    {
                        FIV_MAX = FIV;
                    }
                }
                if (RLED || ICG)
                {
                    FIR_Real = SummFluor / SummWhite;
                    FIR = FIR_Real / FIR_norma;
                    //deltaSum = FIR;
                    if (FIR > FIR_MAX)
                    {
                        FIR_MAX = FIR;
                    }
                }

                if (GLED)
                {
                    FIG_Real = SummFluor / SummWhite;
                    FIG = FIG_Real / FIR_norma;
                    //deltaSum = FIG;
                    if (FIG > FIG_MAX)
                    {
                        FIG_MAX = FIG;
                    }
                }

                if (Oxy)
                {
                    //FIR_Real = SummFluor / (SummWhite + SummFluor);
                    FIR_Real = SummWhite / (SummWhite + SummFluor);
                    //if (FIR_Real > 1)
                    //{
                    //    FIR_Real = SummWhite / SummFluor;
                    //}
                    FIR = FIR_Real;
                    if (FIR > FIR_MAX)
                    {
                        FIR_MAX = FIR;
                    }
                }
            }

            FIcounter += 1;


            //if (TailKiller.ThreadState != System.Threading.ThreadState.Running)
            //    try
            //    {
            //        TailKiller.Start();
            //    }
            //    catch { }
            //FI += FI;
            if (FIcounter == averageLimit)
            {
                //FI = FI / averageLimit;
                FIV_string = String.Format("{0:F2}", FIV);
                FIR_string = String.Format("{0:F2}", FIR);
                FIG_string = String.Format("{0:F2}", FIG);

                FIV_Label.Content = FIV_string;
                FIR_Label.Content = FIR_string;
                FIG_Label.Content = FIG_string;

                FIV_MAX_string = String.Format("MAX {0:F2}", FIV_MAX);
                FIR_MAX_string = String.Format("MAX {0:F2}", FIR_MAX);
                FIG_MAX_string = String.Format("MAX {0:F2}", FIG_MAX);

                FIV_MAX_Label.Content = FIV_MAX_string;
                FIR_MAX_Label.Content = FIR_MAX_string;
                FIG_MAX_Label.Content = FIG_MAX_string;

                bleaching_viol = 100.0 - (FIV / FIV_MAX) * 100.0;
                bleaching_red = 100.0 - (FIR / FIR_MAX) * 100.0;
                bleaching_green = 100.0 - (FIG / FIG_MAX) * 100.0;

                //if((bleaching_red >= desiredBleaching) && (bleachingMeas == 1))
                //{
                //    d.Stop();
                //    CMD = "T_OFF";
                //    SendCMD();
                //    Stopwatch_Label.Content += " STOP";
                //    radioButtonRedLED.IsChecked = true;
                //    bleachingMeas = 0;
                //}

                bleaching_viol_string = String.Format("{0:F2}%", bleaching_viol);
                bleaching_red_string = String.Format("{0:F2}%", bleaching_red);
                bleaching_green_string = String.Format("{0:F2}%", bleaching_green);

                bleaching_viol_Label.Content = bleaching_viol_string;
                bleaching_red_Label.Content = bleaching_red_string;
                bleaching_green_Label.Content = bleaching_green_string;

                FIcounter = 0;
                //FI = 0;
            }
            //sss = String.Format("{0:F1}", FI);
            //FIR_string = String.Format("{0:F1}", FI);
            //FIV_Label.Content = FI_string;
            sumSred = deltaSum;
            Debug.WriteLine(deltaSum);
            Debug.WriteLine(sumSred);
            deltaSum = 0;
            wb.Unlock(); wb1.Unlock(); wb2.Unlock();
            return wb;
        }

        static (byte r, byte g, byte b) HSVToRGB(int h, double s, double v)
        {
            byte r = 0, g = 0, b = 0;

            // Преобразование HSV в RGB
            byte i = (byte)((h / 60) % 6);
            double f = (h / 60.0) - Math.Floor(h / 60.0);
            byte p = (byte)(v * 255 * (1 - s));
            byte q = (byte)(v * 255 * (1 - f * s));
            byte t = (byte)(v * 255 * (1 - (1 - f) * s));
            byte val = (byte)(v * 255);

            switch (i)
            {
                case 0: r = val; g = t; b = p; break;
                case 1: r = q; g = val; b = p; break;
                case 2: r = p; g = val; b = t; break;
                case 3: r = p; g = q; b = val; break;
                case 4: r = t; g = p; b = val; break;
                case 5: r = val; g = p; b = q; break;
            }

            return (r, g, b);
        }

        public void FillRainbow(int h)
        {
            //Debug.WriteLine("Filling the rainbow");
            for (int i = 0; i < 360; i++)
                Rainbow[i] = HSVToRGB(360 - i, 1, 1);

        }

        public void CutGraphPointsTail()
        {
            GraphPoint ppp = GraphPoints[GraphPoints.Count - 1];
            if (double.IsNaN(ppp.sumSred) || double.IsInfinity(ppp.sumSred) || Math.Abs(ppp.sumSred) > 10e20 || Math.Abs(ppp.sumSred) < 10e-20)
                ppp.sumSred = 0;
            double thePast = (DateTime.Now - ProgrammStarted).TotalMilliseconds - 600000;
            GraphPoints.RemoveAll((k) => { return k.millisecond < thePast; });
            if ((DateTime.Now - DebugGap).TotalMilliseconds > 3000)
            {
                DebugGap = DateTime.Now;
                if (GraphPoints.Count > 1)
                    DebugLabel.Dispatcher.Invoke(() => DebugLabel.Content = string.Format("{0}, {1:00.0}", ppp.millisecond, ppp.sumSred));
            }

            //Update chart
            chart.Invoke(new Action(() => chart.Series[0].Add(ppp.millisecond, ppp.sumSred)));
        }


        System.Threading.Thread TailKiller;
        DateTime DebugGap = DateTime.Now;
        public class GraphPoint
        {
            public double millisecond;
            //public double deltaSum;
            public double sumSred;
        }
        public List<GraphPoint> GraphPoints = new List<GraphPoint>();
        DateTime ProgrammStarted = DateTime.Now;

        private System.Drawing.Bitmap BitmapFromWriteableBitmap(WriteableBitmap writeBmp)
        {
            System.Drawing.Bitmap bmp;
            using (MemoryStream outStream = new MemoryStream())
            {
                BitmapEncoder enc = new BmpBitmapEncoder();
                enc.Frames.Add(BitmapFrame.Create((BitmapSource)writeBmp));
                enc.Save(outStream);
                bmp = new System.Drawing.Bitmap(outStream);
            }
            return bmp;
        }

        public static BitmapSource bitmap2bitmasource(System.Drawing.Bitmap bitmap)
        {
            var bitmapData = bitmap.LockBits(
                new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
                System.Drawing.Imaging.ImageLockMode.ReadOnly, bitmap.PixelFormat);

            var bitmapSource = BitmapSource.Create(
                bitmapData.Width, bitmapData.Height,
                bitmap.HorizontalResolution, bitmap.VerticalResolution,
                PixelFormats.Bgr24, null,
                bitmapData.Scan0, bitmapData.Stride * bitmapData.Height, bitmapData.Stride);

            bitmap.UnlockBits(bitmapData);

            return bitmapSource;
        }

        private void button_Click(object sender, RoutedEventArgs e)
        {
            Refreshing = true;
            if (SpinCamColor == null)
                return;
            try
            {
                SpinCamColor.BeginAcquisition();

            }
            catch { }
        }

        private void ButtonNorma_Click(object sender, RoutedEventArgs e)
        {
            bool NoLight = (bool)radioButtonNoLight.IsChecked;
            bool GreenFlu = (bool)radioButtonGreen.IsChecked;
            bool RedFlu = (bool)radioButtonRed.IsChecked;
            bool R2G = (bool)radioButtonR2G.IsChecked;
            bool R_G = (bool)radioButtonR_G.IsChecked;
            bool Grayed = (bool)radioButtonGray.IsChecked;
            bool Pseudo = (bool)radioButtonHeatmap.IsChecked;
            bool Oxy = (bool)radioButtonOxy.IsChecked;
            bool RLED = (bool)radioButtonRedLED.IsChecked;
            bool BOTH = (bool)radioButtonBothLEDs.IsChecked;
            bool ICG = (bool)radioButtonICG.IsChecked;
            bool Sequent = (bool)radioButtonSeq.IsChecked;

            bool OxyAlter = (bool)CheckBoxOxyAlter.IsChecked;
            //if (SpinCamColor == null)
            //    return;

            if (GreenFlu || RedFlu || R2G || R_G)
            {
                try
                {
                    FIV_norma = FIV;
                    FIV_MAX = 0;
                    //System.Windows.MessageBox.Show("FIV_norma = " + FIV_norma.ToString(), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                catch { }
            }
            if (RLED || ICG)
            {
                try
                {
                    FIR_norma = FIR;
                    FIR_MAX = 0;
                    //System.Windows.MessageBox.Show("FIR_norma = " + FIR_norma.ToString(), "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                catch { }
            }
        }

        private void button1_Click(object sender, RoutedEventArgs e)
        {
            if (SpinCamColor == null)
                return;
            Refreshing = false;
            try
            {
                SpinCamColor.EndAcquisition();
            }
            catch { }
        }

        PropertyGridWindow prop = null;
        GUIFactory AcquisitionGUI = null;
        private void button2_Click(object sender, RoutedEventArgs e)
        {
            if (AcquisitionGUI == null)
            {
                AcquisitionGUI = new GUIFactory();
                AcquisitionGUI.ConnectGUILibrary(SpinCamColor);
            }
            if (this.prop == null)
            {
                prop = AcquisitionGUI.GetPropertyGridWindow();
                prop.Connect(SpinCamColor);
            }
            prop.ShowModal();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (SpinCamColor != null)
                if (SpinCamColor.IsStreaming())
                    SpinCamColor.EndAcquisition();

            //if (RefreshThread.IsAlive)
            try
            {
                RefreshThread.Abort();
            }
            catch { }
            e.Cancel = false;
        }



        private void button3_Click(object sender, RoutedEventArgs e)
        {
            p.Close();
            try
            {
                    SpinCamColor.EndAcquisition();
            }
            catch { }
            try
            {

                RefreshThread.Abort();
            }
            catch { }
            System.Windows.Application.Current.Shutdown();
        }

        private void SavingButton_Click(object sender, RoutedEventArgs e)    //Save button
        {
            bool pseudo = (bool)radioButtonHeatmap.IsChecked;
            bool GreenFlu = (bool)radioButtonGreen.IsChecked;
            bool RedFlu = (bool)radioButtonRed.IsChecked;
            bool R2G = (bool)radioButtonR2G.IsChecked;
            bool R_G = (bool)radioButtonR_G.IsChecked;
            bool Oxy = (bool)radioButtonOxy.IsChecked;
            bool Sequent = (bool)radioButtonSeq.IsChecked;
            bool RLED = (bool)radioButtonRedLED.IsChecked;
            bool BOTH = (bool)radioButtonBothLEDs.IsChecked;
            bool ICG = (bool)radioButtonICG.IsChecked;
            bool GLED = (bool)radioButtonGreenLED.IsChecked;
            //bool Grayed = (bool)radioButtonGray.IsChecked;

            if (CheckBoxSeqEnabled.IsChecked == true)
                isSerial = "ser_";
            else
                isSerial = "";

            try
            {
                BitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create((BitmapSource)background));
                DateTime d = DateTime.Now;
                string Filename = @"D:\MEDIA\" + String.Format("{0}_{1}_{2}_{3}_{4}_{5}_{6}_{7}.PNG",
                    d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Millisecond,
                    !(bool)DrawDiffCheckBox.IsChecked ? "Preview" : ("White" + "_Coef" + ampCur)
                    );
                using (var fileStream = new System.IO.FileStream(Filename, System.IO.FileMode.Create))
                {
                    encoder.Save(fileStream);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Error saving picture: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            try
            {
                BitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create((BitmapSource)UV));
                DateTime d = DateTime.Now;
                string Filename = @"D:\MEDIA\" + String.Format("{0}_{1}_{2}_{3}_{4}_{5}_{6}_{7}.PNG",
                   d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Millisecond,
                   !(bool)DrawDiffCheckBox.IsChecked ? "Preview" : ("FLUOR" + "_Coef" + ampCur)
                   );
                using (var fileStream = new System.IO.FileStream(Filename, System.IO.FileMode.Create))
                {
                    encoder.Save(fileStream);
                }
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Error saving picture: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            if (GreenFlu || RedFlu || R2G || R_G)
            {

                try
                {
                    WriteableBitmap frameSource = FindColoredDifference(convertedImage, PrevConvertedImage, 1);
                    System.Drawing.Bitmap bmp;
                    bmp = BitmapFromWriteableBitmap(frameSource);
                    Graphics gr = Graphics.FromImage(bmp);
                    gr.DrawString(FIV_string, new Font("Tahoma", fontSize), System.Drawing.Brushes.Blue, 0, 0);
                    gr.DrawString(temperature, new Font("Tahoma", fontSize), System.Drawing.Brushes.Blue, 0, 70);
                    BitmapFrame frame = BitmapFrame.Create(frameSource);
                    DateTime d = DateTime.Now;
                    string Filename = @"D:\MEDIA\" + String.Format("{0}_{1}_{2}_{3}_{4}_{5}_{6}_{7}.PNG",
                        d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Millisecond,
                        !(bool)DrawDiffCheckBox.IsChecked ? "Preview" : ("Fluo_" + "Green" + "_Coef" + ampCur + "_FIV_" + FIV_string)
                        );
                    bmp.Save(Filename);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show("Error saving picture: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                try
                {
                    WriteableBitmap frameSource = FindColoredDifference(convertedImage, PrevConvertedImage, 2);
                    System.Drawing.Bitmap bmp;
                    bmp = BitmapFromWriteableBitmap(frameSource);
                    Graphics gr = Graphics.FromImage(bmp);
                    gr.DrawString(FIV_string, new Font("Tahoma", fontSize), System.Drawing.Brushes.Blue, 0, 0);
                    gr.DrawString(temperature, new Font("Tahoma", fontSize), System.Drawing.Brushes.Blue, 0, 70);
                    BitmapFrame frame = BitmapFrame.Create(frameSource);
                    DateTime d = DateTime.Now;
                    string Filename = @"D:\MEDIA\" + String.Format("{0}_{1}_{2}_{3}_{4}_{5}_{6}_{7}.PNG",
                        d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Millisecond,
                        !(bool)DrawDiffCheckBox.IsChecked ? "Preview" : (isSerial + "Fluo_" + "Red" + "_Coef" + ampCur + "_FIV_" + FIV_string)
                        );
                    bmp.Save(Filename);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show("Error saving picture: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                try
                {
                    WriteableBitmap frameSource = FindColoredDifference(convertedImage, PrevConvertedImage, 3);

                    System.Drawing.Bitmap bmp;
                    bmp = BitmapFromWriteableBitmap(frameSource);
                    Graphics gr = Graphics.FromImage(bmp);
                    gr.DrawString(FIV_string, new Font("Tahoma", fontSize), System.Drawing.Brushes.Blue, 0, 0);
                    gr.DrawString(temperature, new Font("Tahoma", fontSize), System.Drawing.Brushes.Blue, 0, 70);
                    DateTime d = DateTime.Now;
                    string Filename = @"D:\MEDIA\" + String.Format("{0}_{1}_{2}_{3}_{4}_{5}_{6}_{7}.PNG",
                        d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Millisecond,
                        !(bool)DrawDiffCheckBox.IsChecked ? "Preview" : ("Fluo_" + "R2G" + "_Coef" + ampCur * additionalCoef + "_FIV_" + FIV_string)
                        );
                    bmp.Save(Filename);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show("Error saving picture: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                try
                {
                    WriteableBitmap frameSource = FindColoredDifference(convertedImage, PrevConvertedImage, 5);

                    System.Drawing.Bitmap bmp;
                    bmp = BitmapFromWriteableBitmap(frameSource);
                    Graphics gr = Graphics.FromImage(bmp);
                    gr.DrawString(FIV_string, new Font("Tahoma", fontSize), System.Drawing.Brushes.Blue, 0, 0);
                    gr.DrawString(temperature, new Font("Tahoma", fontSize), System.Drawing.Brushes.Blue, 0, 70);
                    DateTime d = DateTime.Now;
                    string Filename = @"D:\MEDIA\" + String.Format("{0}_{1}_{2}_{3}_{4}_{5}_{6}_{7}.PNG",
                        d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Millisecond,
                        !(bool)DrawDiffCheckBox.IsChecked ? "Preview" : ("Fluo_" + "R-G" + "_Coef" + ampCur * 1 + "_FIV_" + FIV_string)
                        );
                    bmp.Save(Filename);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show("Error saving picture: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            if (RLED)
            {
                try
                {
                    WriteableBitmap frameSource = FindColoredDifference(convertedImage, PrevConvertedImage, 6);

                    Bitmap bmp = BitmapFromWriteableBitmap(frameSource);
                    Graphics gr = Graphics.FromImage(bmp);
                    gr.DrawString(FIR_string, new Font("Tahoma", fontSize), System.Drawing.Brushes.Red, 0, 0);
                    gr.DrawString(temperature, new Font("Tahoma", fontSize), System.Drawing.Brushes.Red, 0, 70);
                    Debug.WriteLine(FI_string);
                    DateTime d = DateTime.Now;
                    string Filename = @"D:\MEDIA\" + String.Format("{0}_{1}_{2}_{3}_{4}_{5}_{6}_{7}.PNG",
                        d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Millisecond,
                        !(bool)DrawDiffCheckBox.IsChecked ? "Preview" : (isSerial + "RLED" + "_Coef" + ampCur * 1 + "_FIR_" + FIR_string)
                        );
                    bmp.Save(Filename);
                }

                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show("Error saving picture: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }


            }

            if (GLED)
            {
                try
                {
                    WriteableBitmap frameSource = FindColoredDifference(convertedImage, PrevConvertedImage, 11);

                    Bitmap bmp = BitmapFromWriteableBitmap(frameSource);
                    Graphics gr = Graphics.FromImage(bmp);
                    gr.DrawString(FIR_string, new Font("Tahoma", fontSize), System.Drawing.Brushes.Green, 0, 0);
                    gr.DrawString(temperature, new Font("Tahoma", fontSize), System.Drawing.Brushes.Green, 0, 70);
                    Debug.WriteLine(FI_string);
                    DateTime d = DateTime.Now;
                    string Filename = @"D:\MEDIA\" + String.Format("{0}_{1}_{2}_{3}_{4}_{5}_{6}_{7}.PNG",
                        d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Millisecond,
                        !(bool)DrawDiffCheckBox.IsChecked ? "Preview" : (isSerial + "GLED" + "_Coef" + ampCur * 1 + "_FIG_" + FIG_string)
                        );
                    bmp.Save(Filename);
                }

                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show("Error saving picture: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }


            }

            if (BOTH)
            {
                try
                {
                    WriteableBitmap frameSource = FindColoredDifference(convertedImage, PrevConvertedImage, 7);
                    System.Drawing.Bitmap bmp;
                    bmp = BitmapFromWriteableBitmap(frameSource);
                    Graphics gr = Graphics.FromImage(bmp);
                    gr.DrawString(FIR_string, new Font("Tahoma", fontSize), System.Drawing.Brushes.White, 0, 0);
                    DateTime d = DateTime.Now;
                    string Filename = @"D:\MEDIA\" + String.Format("{0}_{1}_{2}_{3}_{4}_{5}_{6}_{7}.PNG",
                        d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Millisecond,
                        !(bool)DrawDiffCheckBox.IsChecked ? "Preview" : ("BOTH" + "_Coef" + ampCur * 1 + "_FIR_" + FIR_string)
                        );
                    bmp.Save(Filename);
                }

                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show("Error saving picture: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            if (ICG)
            {
                try
                {
                    WriteableBitmap frameSource = FindColoredDifference(convertedImage, PrevConvertedImage, 8);

                    System.Drawing.Bitmap bmp;
                    bmp = BitmapFromWriteableBitmap(frameSource);
                    Graphics gr = Graphics.FromImage(bmp);
                    gr.DrawString(FIR_string, new Font("Tahoma", fontSize), System.Drawing.Brushes.White, 0, 0);
                    gr.DrawString(temperature, new Font("Tahoma", fontSize), System.Drawing.Brushes.White, 0, 70);
                    DateTime d = DateTime.Now;
                    string Filename = @"D:\MEDIA\" + String.Format("{0}_{1}_{2}_{3}_{4}_{5}_{6}_{7}.PNG",
                        d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Millisecond,
                        !(bool)DrawDiffCheckBox.IsChecked ? "Preview" : ("IR" + "_Coef" + ampCur * 1 + "_FIR_" + FIR_string)
                        );
                    bmp.Save(Filename);
                }

                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show("Error saving picture: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            if (Oxy)
            {
                try
                {
                    WriteableBitmap frameSource = FindColoredDifference(convertedImage, PrevConvertedImage, 10);
                    System.Drawing.Bitmap bmp;
                    bmp = BitmapFromWriteableBitmap(frameSource);
                    Graphics gr = Graphics.FromImage(bmp);
                    gr.DrawString(FIR_string, new Font("Tahoma", fontSize), System.Drawing.Brushes.White, 0, 0);
                    DateTime d = DateTime.Now;
                    string Filename = @"D:\MEDIA\" + String.Format("{0}_{1}_{2}_{3}_{4}_{5}_{6}_{7}.PNG",
                        d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Millisecond,
                        !(bool)DrawDiffCheckBox.IsChecked ? "Preview" : ("Oxy_" + "_Coef" + ampCur * 1 + "_FIR_" + FIR_string)
                        );
                    bmp.Save(Filename);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show("Error saving picture: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }



        private void ButtonNorma_Click_1(object sender, RoutedEventArgs e)
        {
            FI_norma = deltaSum;
        }

        void HsvToRgb(double h, double S, double V, out int r, out int g, out int b)
        {
            double H = h;
            while (H < 0) { H += 360; }
            ;
            while (H >= 360) { H -= 360; }
            ;
            double R, G, B;
            if (V <= 0)
            { R = G = B = 0; }
            else if (S <= 0)
            {
                R = G = B = V;
            }
            else
            {
                double hf = H / 60.0;
                int i = (int)Math.Floor(hf);
                double f = hf - i;
                double pv = V * (1 - S);
                double qv = V * (1 - S * f);
                double tv = V * (1 - S * (1 - f));
                switch (i)
                {

                    // Red is the dominant color

                    case 0:
                        R = V;
                        G = tv;
                        B = pv;
                        break;

                    // Green is the dominant color

                    case 1:
                        R = qv;
                        G = V;
                        B = pv;
                        break;
                    case 2:
                        R = pv;
                        G = V;
                        B = tv;
                        break;

                    // Blue is the dominant color

                    case 3:
                        R = pv;
                        G = qv;
                        B = V;
                        break;
                    case 4:
                        R = tv;
                        G = pv;
                        B = V;
                        break;

                    // Red is the dominant color

                    case 5:
                        R = V;
                        G = pv;
                        B = qv;
                        break;

                    // Just in case we overshoot on our math by a little, we put these here. Since its a switch it won't slow us down at all to put these here.

                    case 6:
                        R = V;
                        G = tv;
                        B = pv;
                        break;
                    case -1:
                        R = V;
                        G = pv;
                        B = qv;
                        break;

                    // The color is not defined, we should throw an error.

                    default:
                        //LFATAL("i Value error in Pixel conversion, Value is %d", i);
                        R = G = B = V; // Just pretend its black/white
                        break;
                }
            }
            r = Clamp((int)(R * 255.0));
            g = Clamp((int)(G * 255.0));
            b = Clamp((int)(B * 255.0));
        }

        /// <summary>
        /// Clamp a value to 0-255
        /// </summary>
        int Clamp(int i)
        {
            if (i < 0) return 0;
            if (i > 255) return 255;
            return i;
        }

        public void ComboboxPorts_DropDownOpened(object sender, EventArgs e)
        {
            string[] ports = SerialPort.GetPortNames();
            foreach (string comport in ports)
            {
                ComboboxPorts.Items.Add(comport);
            }
        }

        private void buttonPortOpen_Click(object sender, EventArgs e)
        {
            if (p != null)
                if (p.IsOpen)
                    return;

            try
            {
                if (ComboboxPorts.SelectedItem == null)
                    p = new SerialPort("COM14", PortSpeed);
                else
                    p = new SerialPort(ComboboxPorts.SelectedItem.ToString(), PortSpeed);
                p.Open();
                p.DtrEnable = true;
                p.RtsEnable = true;
                if (p.IsOpen)
                    return;
            }
            catch
            {
                System.Windows.MessageBox.Show("Failed to open port. Sorry (");
                KeepReading();
                return;
            }
            if (p.IsOpen)
                return;
        }

        private void SaveC_Click(object sender, RoutedEventArgs e)
        {
            int amp = (int)(AmplificationSlider.Value);
            if (radioButtonRed.IsChecked == true)
            {
                ampRed = amp;
            }
            if (radioButtonGreen.IsChecked == true)
            {
                ampGreen = amp;
            }
            if (radioButtonR2G.IsChecked == true)
            {
                ampR2G = amp;
            }
            if (radioButtonR_G.IsChecked == true)
            {
                ampR_G = amp;
            }
            if (radioButtonRedLED.IsChecked == true)
            {
                ampRLED = amp;
            }
            if (radioButtonICG.IsChecked == true)
            {
                ampICG = amp;
            }
            if (radioButtonOxy.IsChecked == true)
            {
                ampOxy = amp;
            }
            if (radioButtonBothLEDs.IsChecked == true)
            {
                ampBOTH = amp;
            }
            if (radioButtonGreenLED.IsChecked == true)
            {
                ampGLED = amp;
            }

        }

        void KeepReading()
        {
            for (; ; )
            {
                if (p != null)
                    if (p.IsOpen)
                    {
                        if (p.BytesToRead > 0)
                        {
                            string tt = p.ReadExisting();
                            //textBox2.Invoke(new Action(() =>
                            //{
                            //    CommaCount += tt.Count((x) => x == ',');
                            //    Buf += tt;
                            //    textBox2.Text = tt;
                            //    toolStripStatusLabel2.Text = "Reads = " + (++ReadsCount).ToString();
                            //}));
                        }
                        Thread.Sleep(5);
                    }
                    else break;
                else break;
            }
        }

        private void ShowGraphButton_Click(object sender, RoutedEventArgs e)
        {
            if (GraphGrid.Visibility != System.Windows.Visibility.Visible)
            {
                GraphGrid.Visibility = System.Windows.Visibility.Visible;
                isPlot = 1;
                d.Restart();
            }
            else
            {
                GraphGrid.Visibility = System.Windows.Visibility.Hidden;
                isPlot = 0;
                d.Stop();
            }                
        }

        private void button6_Click(object sender, RoutedEventArgs e)
        {
            chart.BackColor = Color.FromArgb(255, 255, 255, 0);
        }

        private void button5_Click(object sender, RoutedEventArgs e)
        {
            chart.Walls.Back.Transparent = true;
            chart.Axes.Bottom.Labels.Color = chart.Axes.Bottom.Labels.Color = Color.White;
            chart.Axes.Bottom.Ticks.DrawingPen.Color = Color.Blue;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            SwitchToSmallScreen();
        }

        void SwitchToSmallScreen()
        {
            var allMonitors = System.Windows.Forms.Screen.AllScreens;

            // ���� ������ �������, FUllHD ��� �������
            var secondMonitor = allMonitors.FirstOrDefault(m => !m.Primary && ((float)m.Bounds.Width / m.Bounds.Height < 1.78) && ((float)m.Bounds.Width / m.Bounds.Height > 1.76));

            // ���� ������ ������� ������, ������������� ���� �� ���� �������
            if (secondMonitor != null)
            {
                //mainWindow.Left = secondMonitor.WorkingArea.Left;
                //mainWindow.Top = secondMonitor.WorkingArea.Top;

                ResizeMode = ResizeMode.CanResize;
                WindowState = WindowState.Normal;
                Left = secondMonitor.WorkingArea.Left; Top = secondMonitor.WorkingArea.Top;
                WindowState = WindowState.Maximized;
                ResizeMode = ResizeMode.NoResize;
            }
        }

        private void Slider_Gain_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {

        }

        private void Slider_Exposure_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {

        }

        private void CheckBoxSeqEnabled_Checked(object sender, RoutedEventArgs e)
        {

            bleachingMeas = 1;
            startFrames = 1;
            framesCounter = 0;
            SeqEnabled = true;
            radioButtonSeq.IsChecked = true;
            CMD = "T_OFF";
            SendCMD();
        }

        private void CheckBoxSeqEnabled_Unchecked(object sender, RoutedEventArgs e)
        {
            CMD = "T_OFF";
            startFrames = 0;
            SendCMD();
        }

        private void BleachingTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            //desiredBleaching = int(BleachingTextBox.Text);
            desiredBleaching = int.Parse(BleachingTextBox.Text);
            desiredBleaching = Convert.ToInt32(BleachingTextBox.Text);
        }

        private void buttonSetFocus_Click(object sender, RoutedEventArgs e)
        {
            CMD = "AF";
            SendCMD();
        }

        private void buttonTeplovizor_Click(object sender, RoutedEventArgs e)
        {
            CMD = "TSHOT";
            SendCMD();

                //temperature = p.ReadExisting();
                //Pyrometer_Label.Content = temperature;
                //response = p.ReadLine();
                
                response = p.ReadExisting();
            Debug.WriteLine(response);
            try
                {
                    var ss = response.Split(new[] { '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
                    Debug.WriteLine(ss.Length);

                if (ss.Length > ThermoHeight * ThermoWidth)
                {
                    int[,] data = new int[ThermoHeight, ThermoWidth];
                        for (int i = 1, x = 0, y = 0; i <= data.Length; i++, x++)
                        {
                            if (x == ThermoWidth)
                            {
                                y++; x = 0;
                            }
                            data[y, x] = int.Parse(ss[i]);
                        tSum += data[y, x];
                            //Debug.WriteLine(data[y, x]);
                            //Debug.WriteLine(ss[i]);
                        }
                    tSred = tSum / 768;
                    temperature = tSred.ToString();
                    Pyrometer_Label.Content = temperature;
                    tSum = 0;

                    //flip left-right
                    //int[,] data1 = new int[ThermoHeight, ThermoWidth];
                    ////if (RotatePictures)
                    ////    for (int y = 0, y2 = ThermoHeight - 1; y < ThermoHeight; y++, y2--)
                    ////        for (int x = 0, x2 = ThermoWidth - 1; x < ThermoWidth; x++, x2--)
                    ////            data1[y, x] = data[y2, x];
                    ////else
                    //for (int y = 0; y < ThermoHeight; y++)
                    //    for (int x = 0, x2 = ThermoWidth - 1; x < ThermoWidth; x++, x2--)
                    //    {
                    //        data1[y, x] = data[y, x2];
                    //        //Debug.WriteLine(data1[y, x]);
                    //    }


                    //Dispatcher.Invoke(() => DisplayHeatMap(data1));
                    DisplayHeatMap(data);
                }
                //string temp = ss[0];
            }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error updating dataGrid: {ex.Message}");
                }
            
        }

        private void CheckBoxAutofocus_Checked(object sender, RoutedEventArgs e)
        {
            CMD = "AFON";
            SendCMD();
        }

        private void CheckBoxAutofocus_Unchecked(object sender, RoutedEventArgs e)
        {
            CMD = "AFOFF";
            SendCMD();
        }

        private void CheckBoxTeplovizor_Checked(object sender, RoutedEventArgs e)
        {
            CMD = "TEPLON";
            SendCMD();
        }

        private void CheckBoxTeplovizor_Unchecked(object sender, RoutedEventArgs e)
        {
            CMD = "TEPLOFF";
            SendCMD();
        }

        //private void button_Click(object sender, RoutedEventArgs e)
        //{

        //}

        private void CheckBoxAutofocus_Checked_1(object sender, RoutedEventArgs e)
        {

        }


        //private void RadioButtonGreen_Checked(object sender, RoutedEventArgs e)
        //{

        //}
        //        if (ReadSensorData)
        //                    try
        //                    {
        //                        var ss = response.Split(new[] { '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries);
        //                        if (ss.Length > ThermoHeight* ThermoWidth)
        //                        {
        //                            int[,] data = new int[ThermoHeight, ThermoWidth];
        //                            for (int i = 1, x = 0, y = 0; i <= data.Length; i++, x++)
        //                            {
        //                                if (x == ThermoWidth)
        //                                {
        //                                    y++; x = 0;
        //                                }
        //    data[y, x] = int.Parse(ss[i]);
        //}

        ////flip left-right
        //int[,] data1 = new int[ThermoHeight, ThermoWidth];
        //if (RotatePictures)
        //    for (int y = 0, y2 = ThermoHeight - 1; y < ThermoHeight; y++, y2--)
        //        for (int x = 0, x2 = ThermoWidth - 1; x < ThermoWidth; x++, x2--)
        //            data1[y, x] = data[y2, x];
        //else
        //    for (int y = 0; y < ThermoHeight; y++)
        //        for (int x = 0, x2 = ThermoWidth - 1; x < ThermoWidth; x++, x2--)
        //            data1[y, x] = data[y, x2];
        //Dispatcher.Invoke(() => DisplayHeatMap(data1));
        //                        }
        //                        string temp = ss[0];
        //                    }
        //                    catch (Exception e)
        //                    {
        //                        Debug.WriteLine($"Error updating dataGrid: {e.Message}");
        //                    }


        void DisplayHeatMap(int[,] heatMapData)
        {
            Debug.WriteLine("entered DisplayHeatmap");
            const int heatMapWidth = 32;
            const int heatMapHeight = 24;
            const int outputWidth = 320;
            const int outputHeight = 240;
            //int Max = heatMapData.Cast<int>().Max(), Min = heatMapData.Cast<int>().Min();
            //int Range = Max - Min + 1;            
            int Max = 50, Min = 20;
            int Range = Max - Min + 1;

            // Создаем WriteableBitmap
            WriteableBitmap writeableBitmap = new WriteableBitmap(outputWidth, outputHeight, 96, 96, PixelFormats.Bgra32, null);
            byte[] pixels = new byte[outputWidth * outputHeight * 4];
            //Debug.WriteLine(pixels.Length);

            for (int y = 0, i = 0; y < outputHeight; y++)
            {
                for (int x = 0; x < outputWidth; x++)
                {
                    // Нормализация координат
                    int heatMapX = x * heatMapWidth / outputWidth;
                    int heatMapY = y * heatMapHeight / outputHeight;

                    // Получаем значение из тепловой карты
                    int heatValue = (int)((heatMapData[heatMapY, heatMapX] - Min) * 239.0 / Range);
                    //Debug.WriteLine(heatValue);
                    heatValue = heatValue < 0 ? 0 : heatValue;
                    heatValue = heatValue > 239 ? 239 : heatValue;
                    heatValue += 120;
                    FillRainbow(heatValue);
                    var cc = Rainbow[(int)heatValue];
                    //Debug.WriFillRainbowteLine(cc);
                    //cc = Rainbow[x + 40];

                    // Преобразуем значение в цвет (градус от черного до красного)
                    // Например, можно взять градиент от черного (0, 0, 0) до красного (heatValue, 0, 0) с учетом alpha-канала
                    //pixels[(x + y * outputWidth) * 4 + 0] = cc.b; // Blue
                    //pixels[(x + y * outputWidth) * 4 + 1] = cc.g;     // Green
                    //pixels[(x + y * outputWidth) * 4 + 2] = cc.r;       // Red
                    //pixels[(x + y * outputWidth) * 4 + 3] = 255;     // Alpha

                    pixels[i++] = cc.b; // Blue
                    pixels[i++] = cc.g;     // Green
                    pixels[i++] = cc.r;       // Red
                    pixels[i++] = 255;     // Alpha
                }
            }
            Debug.WriteLine("still alive");
            // Копируем пиксели в WriteableBitmap
            writeableBitmap.WritePixels(new Int32Rect(0, 0, outputWidth, outputHeight), pixels, outputWidth * 4, 0);
            HeatmapImage.Source = writeableBitmap; // Устанавливаем изображение в PictureBox
        }


        static (byte r, byte g, byte b) HSVToRGB_teplovizor(int h, double s, double v)
        {
            byte r = 0, g = 0, b = 0;

            // Преобразование HSV в RGB
            byte i = (byte)((h / 60) % 6);
            double f = (h / 60.0) - Math.Floor(h / 60.0);
            byte p = (byte)(v * 255 * (1 - s));
            byte q = (byte)(v * 255 * (1 - f * s));
            byte t = (byte)(v * 255 * (1 - (1 - f) * s));
            byte val = (byte)(v * 255);

            switch (i)
            {
                case 0: r = val; g = t; b = p; break;
                case 1: r = q; g = val; b = p; break;
                case 2: r = p; g = val; b = t; break;
                case 3: r = p; g = q; b = val; break;
                case 4: r = t; g = p; b = val; break;
                case 5: r = val; g = p; b = q; break;
            }

            return (r, g, b);
        }


        public static void WriteTextToImage(string inputFile, string outputFile, FormattedText text, System.Windows.Point position)
        {
            BitmapImage bitmap = new BitmapImage(new Uri(inputFile)); // inputFile must be absolute path
            DrawingVisual visual = new DrawingVisual();

            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawImage(bitmap, new Rect(0, 0, bitmap.PixelWidth, bitmap.PixelHeight));
                dc.DrawText(text, position);
            }

            RenderTargetBitmap target = new RenderTargetBitmap(bitmap.PixelWidth, bitmap.PixelHeight,
                                                               bitmap.DpiX, bitmap.DpiY, PixelFormats.Default);
            target.Render(visual);

            BitmapEncoder encoder = null;

            switch (System.IO.Path.GetExtension(outputFile))
            {
                case ".png":
                    encoder = new PngBitmapEncoder();
                    break;
                    // more encoders here
            }

            if (encoder != null)
            {
                encoder.Frames.Add(BitmapFrame.Create(target));
                using (FileStream outputStream = new FileStream(outputFile, FileMode.Create))
                {
                    encoder.Save(outputStream);
                }
            }
        }
    }
}
