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

namespace SimplestSpinWPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        IManagedCamera SpinCamColor = null;
        //PropertyGridControl gridControl = new PropertyGridControl();
        byte[] DivideCache = new byte[256 * 256];

        Thread RefreshThread;
        public MainWindow()
        {
            InitializeComponent();

            for (int i = 0; i < 256; i++)
                for (int j = 0; j < 256; j++)
                    if (j == 0)
                        DivideCache[i * 256 + j] = 0;
                    else
                        DivideCache[i * 256 + j] = (byte)(i * additionalCoef / j);

            //LayoutLeft.Children.Add(gridControl);

            //Camera search and initialization
            // Retrieve singleton reference to system object
            ManagedSystem system = new ManagedSystem();


            // Retrieve list of cameras from the system
            IList<IManagedCamera> cameraList = system.GetCameras();

            var BlackFlys = cameraList.Where(c => c.GetTLDeviceNodeMap().GetNode<IString>("DeviceModelName").Value.Contains("Blackfly S")).ToArray();
            //var BlackFlys1 = cameraList.Where(c => c.GetTLDeviceNodeMap().Values.d.Contains("BlackFly S")).ToArray();
            //var Nodes = cameraList.Select(c => c.GetTLDeviceNodeMap()).ToArray();
            //if (BlackFlys.Length < 1)
            //    return;

            //IManagedCamera cam = BlackFlys[0];

            if (cameraList.Count < 1)
            {
                MessageBox.Show("No camera is found!", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                this.Close();
                return;
            }

            IManagedCamera cam = cameraList[0];
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
                        App.Current.Dispatcher.Invoke(new Action(() => { RefreshScreen(); }), DispatcherPriority.Send);
                        this.Title = SpinCamColor.ExposureTime.ToString() + SpinCamColor.DeviceSerialNumber.ToString() + cam.DeviceModelName;
                    }
                }
            }

            cam.EndAcquisition();
            //gridControl.Connect(cam);
            //cam.DeInit();
            //system.Dispose();
            RefreshThread = new Thread(GetImages);
            RefreshThread.Start();
        }

        const int PortSpeed = 115200;
        bool Refreshing = false;
        int i = 0;
        int additionalCoef = 5;
        long LastImageSum = 0;
        public BitmapSource convertedImage = null;
        public BitmapSource PrevConvertedImage = null;
        public BitmapSource redImage = null;
        public BitmapSource greenImage = null;
        public BitmapSource background = null;
        public BitmapSource R2G = null;
        public BitmapSource heatmapR2G = null;
        public BitmapSource heatmapRed = null;
        public BitmapSource heatmapGreen = null;
        public BitmapSource PSEUDO = null;
        public BitmapSource HeatMap = null;
        public BitmapSource bsOut = null;
        int FIcounter = 0;
        int averageLimit = 1;
        int checkNpixelsInCursor = 0;
        double FI_norma = 1;
        double FI = 0;

        public long PrevImageSum = 0;
        private void GetImages()
        {
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
                                        rawImage.ConvertToBitmapSource(PixelFormatEnums.BGR8, rawImage, ColorProcessingAlgorithm.DEFAULT);
                                        PrevConvertedImage = convertedImage;
                                        convertedImage = rawImage.bitmapsource;
                                        i++;
                                        PrevImageSum = LastImageSum;
                                        LastImageSum = FindSum(convertedImage);

                                        RefreshScreen();
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
            long L = (int)wb.Width * (int)wb.Height * 3;
            for (int i = 0; i < L; i += 3)
                //for (int x = 0, i = 0; x < wb.Width; x++)
                //    for (int y = 0; y < wb.Height; y++)
                Sum += bb[i];
            wb.Unlock();
            return Sum;
        }

        unsafe public WriteableBitmap FindColoredDifference(BitmapSource bs1, BitmapSource bs2, byte mode)
        {
            bool GreenFlu = (bool)radioButtonGreen.IsChecked;
            bool RedFlu = (bool)radioButtonRed.IsChecked;
            bool R2G = (bool)radioButtonR2G.IsChecked;
            bool Grayed = (bool)radioButtonGray.IsChecked;
            bool Pseudo = (bool)radioButtonHeatmap.IsChecked;

            if (mode == 1)
                GreenFlu = true;
            if (mode == 2)
                RedFlu = true;
            if (mode == 3)
                R2G = true;

            if (bs1 == null)
                return null;

            WriteableBitmap wb1 = new WriteableBitmap(bs1);
            WriteableBitmap wb2 = new WriteableBitmap(bs2);
            WriteableBitmap wb;

            if (LastImageSum < PrevImageSum)
            {
                wb = new WriteableBitmap(bs1);
                background = wb1;
            }
            else
            {
                wb = new WriteableBitmap(bs2);
                background = wb2;
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
            int difGreen = 0;
            int temp;
            byte res = 0;

            int amp = (int)(AmplificationSlider.Value);
            long L = (int)wb1.Width * (int)wb1.Height * 3;
            int width = (int)wb1.Width * 3;
            int height = (int)wb1.Height;
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
            int w, h;
            int wCursor = 10;
            int wCursor3 = 30;
            int hCursor = 10;
            int firstCursorPixel;
            
            firstCursorPixel = (width * ((height / 2) - (wCursor / 2))) + (width / 2);
            FIcounter += 1;
            fixed (byte* DC = DivideCache)
            {
                for (int b = 0, g = 1, r = 2; b < L; b += 3, r += 3, g += 3)
                {
                    if (GreenFlu)
                        dif = (bb1[g] - bb2[g]);
                    if (RedFlu)
                        dif = (bb1[r] - bb2[r]);

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
                        //difDouble = (difRed / difGreen) * additionalCoef;

                        dif = DC[(difRed << 8) + difGreen];
                        //dif = (byte)(difRed /  difGreen);
                        //dif = DC[(difGreen << 8) + difRed];
                        //dif = (int)difDouble;                       
                    }

                    if (dif < 0)
                        dif = -dif;

                    res = bb[g];
                    if (amp > 0)
                        dif <<= amp;
                    if (amp < 0)
                        dif >>= -amp;

                    temp = res + dif;

                    if (Pseudo)
                    {
                        if (temp > 255)
                        {
                            bb[g] = 255; bb[r] = 255; bb[b] = 255;
                        }
                        else
                        {
                            bb[r] = (byte)(temp << 1); bb[g] = (byte)(temp << 2); bb[b] = (byte)(temp << 4);
                        }
                        if (Grayed)
                            bb[b] = res; bb[r] = res;
                    }
                    else
                    {
                        if (temp > 255)
                            bb[g] = 255;
                        else
                            bb[g] = (byte)(temp);

                        if (Grayed)
                            bb[b] = res; bb[r] = res;
                    }

                    if (Grayed)
                        bb[b] = res; bb[r] = res;
                }
            }
            //firstCursorPixel = 0;
            for (int cursorString = 0; cursorString < hCursor; cursorString += 1)
                for (int b = ((firstCursorPixel + width * cursorString) + 0), g = ((firstCursorPixel + width * cursorString) + 1), r = ((firstCursorPixel + width * cursorString) + 2); b < ((firstCursorPixel + width * cursorString) + wCursor * 3); b += 3, r += 3, g += 3)
                {
                    bb[g] = 0; bb[b] = 0; bb[r] = 0;
                    SummFluor += (bb2[r] - bb1[r]);
                    SummWhite += bb1[r];
                }

            if (FIcounter == averageLimit)
            {
                FI = SummFluor / SummWhite;
                if (FI < 0)
                    FI *= -1;
                FI = FI / FI_norma;
                FIcounter = 0;
                FI_textbox.Text = FI.ToString("F1");
            }

            wb.Unlock(); wb1.Unlock(); wb2.Unlock();
            return wb;
        }


        int Clamp(int i)
        {
            if (i < 0) return 0;
            if (i > 255) return 255;
            return i;
        }

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
            if (SpinCamColor == null)
                return;
            try
            {
                SpinCamColor.BeginAcquisition();
                Refreshing = true;
            }
            catch { }
        }

        private void ButtonNorma_Click(object sender, RoutedEventArgs e)
        {
            if (SpinCamColor == null)
                return;
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

            if (RefreshThread.IsAlive)
                RefreshThread.Abort();
            e.Cancel = false;
        }

        private void button3_Click(object sender, RoutedEventArgs e)
        {
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
            Application.Current.Shutdown();
        }

        private void button4_Click(object sender, RoutedEventArgs e)    //Save button
        {
            bool pseudo = (bool)radioButtonHeatmap.IsChecked;
            bool GreenFlu = (bool)radioButtonGreen.IsChecked;
            bool RedFlu = (bool)radioButtonRed.IsChecked;
            bool R2G = (bool)radioButtonR2G.IsChecked;
            bool Grayed = (bool)radioButtonGray.IsChecked;

            try
            {
                BitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create((BitmapSource)background));
                DateTime d = DateTime.Now;
                string Filename = @"C:\MEDIA\" + String.Format("{0}_{1}_{2}_{3}_{4}_{5}_{6}_{7}.PNG",
                    d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Millisecond,
                    !(bool)DrawDiffCheckBox.IsChecked ? "Preview" : ("White" + "_Coef" + (int)(AmplificationSlider.Value) + "_FI_" + FI_textbox.Text)
                    );
                using (var fileStream = new System.IO.FileStream(Filename, System.IO.FileMode.Create))
                {
                    encoder.Save(fileStream);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error saving picture: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            try
            {
                BitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create((FindColoredDifference(convertedImage, PrevConvertedImage, 1))));
                DateTime d = DateTime.Now;
                string Filename = @"C:\MEDIA\" + String.Format("{0}_{1}_{2}_{3}_{4}_{5}_{6}_{7}.PNG",
                    d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Millisecond,
                    !(bool)DrawDiffCheckBox.IsChecked ? "Preview" : ("Fluo_" + "Green" + "_Coef" + (int)(AmplificationSlider.Value) + "_FI_" + FI_textbox.Text)
                    );
                using (var fileStream = new System.IO.FileStream(Filename, System.IO.FileMode.Create))
                {
                    encoder.Save(fileStream);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error saving picture: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            try
            {
                BitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create((FindColoredDifference(convertedImage, PrevConvertedImage, 2))));
                DateTime d = DateTime.Now;
                string Filename = @"C:\MEDIA\" + String.Format("{0}_{1}_{2}_{3}_{4}_{5}_{6}_{7}.PNG",
                    d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Millisecond,
                    !(bool)DrawDiffCheckBox.IsChecked ? "Preview" : ("Fluo_" + "Red" + "_Coef" + (int)(AmplificationSlider.Value) + "_FI_" + FI_textbox.Text)
                    );
                using (var fileStream = new System.IO.FileStream(Filename, System.IO.FileMode.Create))
                {
                    encoder.Save(fileStream);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error saving picture: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            try
            {
                BitmapEncoder encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create((FindColoredDifference(convertedImage, PrevConvertedImage, 3))));
                DateTime d = DateTime.Now;
                string Filename = @"C:\MEDIA\" + String.Format("{0}_{1}_{2}_{3}_{4}_{5}_{6}_{7}.PNG",
                    d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Millisecond,
                    !(bool)DrawDiffCheckBox.IsChecked ? "Preview" : ("Fluo_" + "R2G" + "_Coef" + (int)(AmplificationSlider.Value) * additionalCoef + "_FI_" + FI_textbox.Text)
                    );
                using (var fileStream = new System.IO.FileStream(Filename, System.IO.FileMode.Create))
                {
                    encoder.Save(fileStream);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error saving picture: " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
