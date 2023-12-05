using System;
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
                FlirCamInit();
            if (InitDAO)
                DAOCamInit();


            if (FLiRCamCount < 1 && DAOCamCount < 1)
            {
                System.Windows.MessageBox.Show("No camera is found!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
        int checkNpixelsInCursor = 0;
        double FI_norma = 1;
        double FI = 0;
        double FI_Real = 0;
        string FI_string = "";
        string fileName4Saving = "";
        string fileNameDecreased = "";
        string _portName = "";
        volatile string CMD = "";
        bool Oxy = false;
        string AIM_color = "blue";

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
                ErrorHappend = "No camera found";

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
                MessageBox.Show("No camera is found!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                                            catch { };
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
                                catch (Exception ex) { Debug.WriteLine("Error output to screen: " + ex.Message); };
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
                    if (CMD == "M1")
                        p.Write("M1\n");
                    if(CMD == "M2")
                        p.Write("M2\n");
                    if (CMD == "M7")
                        p.Write("M7\n");
                    if (CMD == "M6")
                        p.Write("M6\n");
                    if (CMD == "M4")
                        p.Write("M4\n");
                    if (CMD == "M3")
                        p.Write("M3\n");
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

        private void RadioButtonR2G_Checked(object sender, EventArgs e)
        {
            // приводим отправителя к элементу типа RadioButton
            RadioButton radioButtonR2G = (RadioButton)sender;
            if (radioButtonR2G.IsChecked == true)
            {
                CMD = "M1";
                AIM_color = "blue";
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
            // приводим отправителя к элементу типа RadioButton
            RadioButton radioButtonR_G = (RadioButton)sender;
            if (radioButtonR_G.IsChecked == true)
            {
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
            // приводим отправителя к элементу типа RadioButton
            RadioButton radioButtonGreen = (RadioButton)sender;
            if (radioButtonGreen.IsChecked == true)
            {
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

        private void RadioButtonRed_Checked(object sender, EventArgs e)
        {
            // приводим отправителя к элементу типа RadioButton
            RadioButton radioButtonRed = (RadioButton)sender;
            if (radioButtonRed.IsChecked == true)
            {
                AIM_color = "blue";
                filterChange(0);
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
            // приводим отправителя к элементу типа RadioButton
            RadioButton radioButtonOxy = (RadioButton)sender;
            if (radioButtonOxy.IsChecked == true)
            {
                AIM_color = "white";
                filterChange(1);
                CMD = "M4";
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
            // приводим отправителя к элементу типа RadioButton
            RadioButton RadioButtonRedLED = (RadioButton)sender;
            if (radioButtonRedLED.IsChecked == true)
            {
                AIM_color = "red";
                filterChange(0);
                CMD = "M2";
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
            // приводим отправителя к элементу типа RadioButton
            RadioButton radioButtonBothLEDs = (RadioButton)sender;
            if (radioButtonBothLEDs.IsChecked == true)
            {
                AIM_color = "white";
                filterChange(0);
                CMD = "M3";
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
            // приводим отправителя к элементу типа RadioButton
            RadioButton radioButtonICG = (RadioButton)sender;
            if (radioButtonICG.IsChecked == true)
            {
                AIM_color = "blue";
                filterChange(1);
                CMD = "M7";
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
            // приводим отправителя к элементу типа RadioButton
            RadioButton radioButtonSeq = (RadioButton)sender;
            if (radioButtonSeq.IsChecked == true)
            {
                AIM_color = "blue";
                filterChange(0);
                CMD = "M6";
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




            if (mode == 1)
            {
                GreenFlu = true; RedFlu = false; R2G = false; R_G = false; Oxy = false; RLED = false; BOTH = false; ICG = false; Sequent = false;
            }
            if (mode == 2)
            {
                RedFlu = true; GreenFlu = false; R2G = false; R_G = false; Oxy = false; RLED = false; BOTH = false; ICG = false; Sequent = false;
            }
            if (mode == 3)
            {
                R2G = true; GreenFlu = false; RedFlu = false; R_G = false; Oxy = false; RLED = false; BOTH = false; ICG = false; Sequent = false;
            }
            if (mode == 5)
            {
                R_G = true; R2G = false; GreenFlu = false; RedFlu = false; Oxy = false; RLED = false; BOTH = false; ICG = false; Sequent = false;
            }
            if (mode == 6)
            {
                R_G = false; R2G = false; GreenFlu = false; RedFlu = false; Oxy = false; RLED = true; BOTH = false; ICG = false; Sequent = false;
            }
            if (mode == 7)
            {
                R_G = false; R2G = false; GreenFlu = false; RedFlu = false; Oxy = false; RLED = false; BOTH = true; ICG = false; Sequent = false;
            }
            if (mode == 8)
            {
                R_G = false; R2G = false; GreenFlu = false; RedFlu = false; Oxy = false; RLED = false; BOTH = false; ICG = true; Sequent = false;
            }
            if (mode == 9)
            {
                R_G = false; R2G = false; GreenFlu = false; RedFlu = false; Oxy = false; RLED = false; BOTH = false; ICG = false; Sequent = true;
            }
            if (mode == 10)
            {
                R_G = false; R2G = false; GreenFlu = false; RedFlu = false; Oxy = true; RLED = false; BOTH = false; ICG = false; Sequent = false;
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

            int amp = (int)(AmplificationSlider.Value);
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
                for (int b = 0, g = 1, r = 2, i = 0; b < L; b += 3, r += 3, g += 3, i++)
                {
                    if (GreenFlu)
                    {
                        dif = (bb1[g] - bb2[g]);
                    }

                    if (RedFlu)
                    {
                        dif = (bb1[r] - bb2[r]);
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
                    }

                    if (Oxy)
                    {
                        dataRed = bb1[r];
                        dataIR = bb2[r];

                        if (OxyAlter)
                        {
                            dataRed = bb2[r];
                            dataIR = bb1[r];
                        }

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
                    }

                    if (RLED)
                    {
                        dif = bb1[r] - bb2[r];
                        if (dif < 0)
                            dif = -dif;

                    }

                    if (BOTH)
                    {
                        dif = bb1[r] - bb2[r];
                        if (dif < 0)
                            dif = -dif;

                    }


                    if (ICG)
                    {
                        dif = bb1[r] - bb2[r];
                        if (dif < 0)
                            dif = -dif;

                    }

                    if (Sequent)
                    {
                        dif = bb1[r] - bb2[r];
                        if (dif < 0)
                            dif = -dif;

                    }

                    if (dif < 0)
                        dif = -dif;


                    res = bb[g];
                    dif <<= amp;

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
                        int j = temp << 2;
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
            FI_Real = SummFluor / SummWhite;
            FI = FI_Real / FI_norma;
            FIcounter += 1;

            GraphPoints.Add(new GraphPoint { FI_Real = FI_Real, millisecond = (DateTime.Now - ProgrammStarted).TotalMilliseconds });
            if(TailKiller == null)
            {
                TailKiller = new System.Threading.Thread(() => { while (true) { CutGraphPointsTail(); Thread.Sleep(1000); } });
                TailKiller.Start();
                try { chart.Series[0].Clear(); } catch { }
            }
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
                FI_string = String.Format("{0:F1}", FI);
                FI_Label.Content = FI_string;
                FIcounter = 0;
                //FI = 0;
            }
            //sss = String.Format("{0:F1}", FI);
            sss = String.Format("{0:F1}", FI);
            FI_Label.Content = sss;

            wb.Unlock(); wb1.Unlock(); wb2.Unlock();
            return wb;
        }

        public void CutGraphPointsTail()
        {                    
            GraphPoint ppp = GraphPoints[GraphPoints.Count - 1];
            if(double.IsNaN(ppp.FI_Real) || double.IsInfinity(ppp.FI_Real) || Math.Abs(ppp.FI_Real) > 10e20 || Math.Abs(ppp.FI_Real) < 10e-20)
                ppp.FI_Real = 0;
            double thePast = (DateTime.Now - ProgrammStarted).TotalMilliseconds - 600000;
            GraphPoints.RemoveAll((k) => { return k.millisecond < thePast; });
            if ((DateTime.Now - DebugGap).TotalMilliseconds > 3000)
            {
                DebugGap = DateTime.Now;
                if (GraphPoints.Count > 1)
                    DebugLabel.Dispatcher.Invoke(() => DebugLabel.Content = string.Format("{0}, {1:00.0}", ppp.millisecond, ppp.FI_Real));
            }

            //Update chart
            chart.Invoke(new Action( () =>  chart.Series[0].Add(ppp.millisecond, ppp.FI_Real) ));
        }


        System.Threading.Thread TailKiller; 
        DateTime DebugGap = DateTime.Now;
        public class GraphPoint
        {
            public double millisecond;
            public double FI_Real;
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
            //if (SpinCamColor == null)
            //    return;
            try
            {
                FI_norma = FI;
            }
            catch { }
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

        private void button4_Click(object sender, RoutedEventArgs e)    //Save button
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
            //bool Grayed = (bool)radioButtonGray.IsChecked;

            try
            {
                BitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create((BitmapSource)background));
                DateTime d = DateTime.Now;
                string Filename = @"C:\MEDIA\" + String.Format("{0}_{1}_{2}_{3}_{4}_{5}_{6}_{7}.PNG",
                    d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Millisecond,
                    !(bool)DrawDiffCheckBox.IsChecked ? "Preview" : ("White" + "_Coef" + (int)(AmplificationSlider.Value))
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
                string Filename = @"C:\MEDIA\" + String.Format("{0}_{1}_{2}_{3}_{4}_{5}_{6}_{7}.PNG",
                   d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Millisecond,
                   !(bool)DrawDiffCheckBox.IsChecked ? "Preview" : ("FLUOR" + "_Coef" + (int)(AmplificationSlider.Value))
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

                //try
                //{
                //    WriteableBitmap frameSource = FindColoredDifference(convertedImage, PrevConvertedImage, 1);
                //    System.Drawing.Bitmap bmp;
                //    bmp = BitmapFromWriteableBitmap(frameSource);
                //    Graphics gr = Graphics.FromImage(bmp);
                //    gr.DrawString(FI_string, new Font("Tahoma", 5000), System.Drawing.Brushes.Blue, 0, 0);
                //    BitmapFrame frame = BitmapFrame.Create(frameSource);
                //    DateTime d = DateTime.Now;
                //    string Filename = @"C:\MEDIA\" + String.Format("{0}_{1}_{2}_{3}_{4}_{5}_{6}_{7}.PNG",
                //        d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Millisecond,
                //        !(bool)DrawDiffCheckBox.IsChecked ? "Preview" : ("Fluo_" + "Green" + "_Coef" + (int)(AmplificationSlider.Value) + "_FI_" + FI_string)
                //        );
                //    bmp.Save(Filename);
                //}
                //catch (Exception ex)
                //{
                //    System.Windows.MessageBox.Show("Error saving picture: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                //}

                try
                {
                    WriteableBitmap frameSource = FindColoredDifference(convertedImage, PrevConvertedImage, 1);
                    System.Drawing.Bitmap bmp;
                    bmp = BitmapFromWriteableBitmap(frameSource);
                    Graphics gr = Graphics.FromImage(bmp);
                    gr.DrawString(FI_string, new Font("Tahoma", 5000), System.Drawing.Brushes.Blue, 0, 0);
                    BitmapFrame frame = BitmapFrame.Create(frameSource);
                    DateTime d = DateTime.Now;
                    string Filename = @"C:\MEDIA\" + String.Format("{0}_{1}_{2}_{3}_{4}_{5}_{6}_{7}.PNG",
                        d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Millisecond,
                        !(bool)DrawDiffCheckBox.IsChecked ? "Preview" : ("Fluo_" + "Red" + "_Coef" + (int)(AmplificationSlider.Value) + "_FI_" + FI_string)
                        );
                    bmp.Save(Filename);
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show("Error saving picture: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }

                //try
                //{
                //    WriteableBitmap frameSource = FindColoredDifference(convertedImage, PrevConvertedImage, 3);

                //    System.Drawing.Bitmap bmp;
                //    bmp = BitmapFromWriteableBitmap(frameSource);
                //    Graphics gr = Graphics.FromImage(bmp);
                //    gr.DrawString(FI_string, new Font("Tahoma", 5000), System.Drawing.Brushes.Blue, 0, 0);
                //    DateTime d = DateTime.Now;
                //    string Filename = @"C:\MEDIA\" + String.Format("{0}_{1}_{2}_{3}_{4}_{5}_{6}_{7}.PNG",
                //        d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Millisecond,
                //        !(bool)DrawDiffCheckBox.IsChecked ? "Preview" : ("Fluo_" + "R2G" + "_Coef" + (int)(AmplificationSlider.Value) * additionalCoef + "_FI_" + FI_string)
                //        );
                //    bmp.Save(Filename);
                //}
                //catch (Exception ex)
                //{
                //    System.Windows.MessageBox.Show("Error saving picture: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                //}

                //try
                //{
                //    WriteableBitmap frameSource = FindColoredDifference(convertedImage, PrevConvertedImage, 5);

                //    System.Drawing.Bitmap bmp;
                //    bmp = BitmapFromWriteableBitmap(frameSource);
                //    Graphics gr = Graphics.FromImage(bmp);
                //    gr.DrawString(FI_string, new Font("Tahoma", 5000), System.Drawing.Brushes.Blue, 0, 0);
                //    DateTime d = DateTime.Now;
                //    string Filename = @"C:\MEDIA\" + String.Format("{0}_{1}_{2}_{3}_{4}_{5}_{6}_{7}.PNG",
                //        d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Millisecond,
                //        !(bool)DrawDiffCheckBox.IsChecked ? "Preview" : ("Fluo_" + "R-G" + "_Coef" + (int)(AmplificationSlider.Value) * 1 + "_FI_" + FI_string)
                //        );
                //    bmp.Save(Filename);
                //}
                //catch (Exception ex)
                //{
                //    System.Windows.MessageBox.Show("Error saving picture: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                //}
            }

            if (RLED)
            {
                try
                {
                    WriteableBitmap frameSource = FindColoredDifference(convertedImage, PrevConvertedImage, 6);

                    Bitmap bmp = BitmapFromWriteableBitmap(frameSource);
                    Graphics gr = Graphics.FromImage(bmp);
                    gr.DrawString(FI_string, new Font("Tahoma", 5000), System.Drawing.Brushes.Red, 0, 0);
                    Debug.WriteLine(FI_string);
                    DateTime d = DateTime.Now;
                    string Filename = @"C:\MEDIA\" + String.Format("{0}_{1}_{2}_{3}_{4}_{5}_{6}_{7}.PNG",
                        d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Millisecond,
                        !(bool)DrawDiffCheckBox.IsChecked ? "Preview" : ("RLED" + "_Coef" + (int)(AmplificationSlider.Value) * 1 + "_FI_" + FI_string)
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
                    gr.DrawString(FI_string, new Font("Tahoma", 5000), System.Drawing.Brushes.White, 0, 0);
                    DateTime d = DateTime.Now;
                    string Filename = @"C:\MEDIA\" + String.Format("{0}_{1}_{2}_{3}_{4}_{5}_{6}_{7}.PNG",
                        d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Millisecond,
                        !(bool)DrawDiffCheckBox.IsChecked ? "Preview" : ("BOTH" + "_Coef" + (int)(AmplificationSlider.Value) * 1 + "_FI_" + FI_string)
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
                    gr.DrawString(FI_string, new Font("Tahoma", 5000), System.Drawing.Brushes.White, 0, 0);
                    DateTime d = DateTime.Now;
                    string Filename = @"C:\MEDIA\" + String.Format("{0}_{1}_{2}_{3}_{4}_{5}_{6}_{7}.PNG",
                        d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Millisecond,
                        !(bool)DrawDiffCheckBox.IsChecked ? "Preview" : ("IR" + "_Coef" + (int)(AmplificationSlider.Value) * 1 + "_FI_" + FI_string)
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
                    gr.DrawString(FI_string, new Font("Tahoma", 5000), System.Drawing.Brushes.White, 0, 0);
                    DateTime d = DateTime.Now;
                    string Filename = @"C:\MEDIA\" + String.Format("{0}_{1}_{2}_{3}_{4}_{5}_{6}_{7}.PNG",
                        d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Millisecond,
                        !(bool)DrawDiffCheckBox.IsChecked ? "Preview" : ("Oxy_" + "_Coef" + (int)(AmplificationSlider.Value) * 1 + "_FI_" + FI_string)
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
            FI_norma = FI_Real;
        }

        void HsvToRgb(double h, double S, double V, out int r, out int g, out int b)
        {
            double H = h;
            while (H < 0) { H += 360; };
            while (H >= 360) { H -= 360; };
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

        private void ChangeMode_Click(object sender, RoutedEventArgs e)
        {
            if (radioButtonOxy.IsChecked == true) MessageBox.Show(radioButtonOxy.Content.ToString());
            if (radioButtonRedLED.IsChecked == true) MessageBox.Show(radioButtonRedLED.Content.ToString());
            if (radioButtonBothLEDs.IsChecked == true) MessageBox.Show(radioButtonBothLEDs.Content.ToString());
            if (radioButtonICG.IsChecked == true) MessageBox.Show(radioButtonICG.Content.ToString());
            if (radioButtonSeq.IsChecked == true) MessageBox.Show(radioButtonSeq.Content.ToString());
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
            if(GraphGrid.Visibility != System.Windows.Visibility.Visible)
                GraphGrid.Visibility = System.Windows.Visibility.Visible;
            else
                GraphGrid.Visibility = System.Windows.Visibility.Hidden;
        }

        private void button6_Click(object sender, RoutedEventArgs e)
        {
            chart.BackColor = Color.FromArgb(255,0,0,0);
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

            // Ищем второй монитор, FUllHD или похожий
            var secondMonitor = allMonitors.FirstOrDefault(m => !m.Primary && ((float)m.Bounds.Width / m.Bounds.Height < 1.78) && ((float)m.Bounds.Width / m.Bounds.Height > 1.76));

            // Если второй монитор найден, устанавливаем окно на этот монитор
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
