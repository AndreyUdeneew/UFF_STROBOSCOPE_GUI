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
using System.Runtime.InteropServices;
using System.Drawing.Imaging;

namespace SimplestSpinWPF
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        IManagedCamera SpinCamColor = null;
        //PropertyGridControl gridControl = new PropertyGridControl();

        Thread RefreshThread;
        public MainWindow()
        {
            InitializeComponent();

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
        int additionalCoef = 10;
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
                //for (int y = 0; y < wb.Height; y++)
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
            bool pseudo = (bool)radioButtonHeatmap.IsChecked;

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
            //Bitmap heatmap;

            if (LastImageSum < PrevImageSum)
            {
                wb = new WriteableBitmap(bs1);
                background = wb2;
            }
            else
            {
                wb = new WriteableBitmap(bs2);
                background = wb1;
            }
            wb1.Lock(); wb2.Lock(); wb.Lock();
            byte* bb1 = (byte*)wb1.BackBuffer.ToPointer();
            byte* bb2 = (byte*)wb2.BackBuffer.ToPointer();
            byte* bb = (byte*)wb.BackBuffer.ToPointer();

            int dif = 0;
            double difDouble = 0;
            double difRed = 0;
            double difGreen = 0;
            int temp;
            byte res = 0;

            int amp = (int)(AmplificationSlider.Value);
            long L = (int)wb1.Width * (int)wb1.Height * 3;

            for (int b = 0, g = 1, r = 2; b < L; b += 3, r += 3, g += 3)
            {
                if (GreenFlu)
                    dif = (bb1[g] - bb2[g]);
                if (RedFlu)
                    dif = (bb1[r] - bb2[r]);

                if (R2G)
                {
                    difRed = bb1[r] - bb2[r];
                    difGreen = bb1[g] - bb2[g];

                    if (difGreen == 0)
                    {
                        difGreen = 1;
                    }
                    //amp = amp +;
                    //dif = difRed >> difGreen;
                    difDouble = (difRed / difGreen) * additionalCoef;
                    dif = (int)difDouble;
                }
                
                if (dif < 0)
                    dif = -dif;

                res = bb[g];
                if (amp > 0)
                    dif <<= amp;
                if (amp < 0)
                    dif >>= -amp;
                //if (bb[r] + dif > 255) bb[r] = 255; else bb[r] += (byte)dif;
                temp = res + dif;
                if (temp > 255)
                    bb[g] = 255;
                else
                    bb[g] = (byte)(temp);

                if (Grayed)
                    bb[b] = res; bb[r] = res;
            }
            //wb.WritePixels(new Int32Rect(0, 0, (int)wb1.Width, (int)wb1.Height), heatmap, (int)wb1.Width * 3, 0);
            wb.Unlock(); wb1.Unlock(); wb2.Unlock();
            return wb1;
        }

        int Clamp(int i)
        {
            if (i < 0) return 0;
            if (i > 255) return 255;
            return i;
        }

        unsafe public BitmapSource Heatmap(BitmapSource bs)
        {
            WriteableBitmap heatmap;
            WriteableBitmap wbTest;


            heatmap = new WriteableBitmap(bs);
            wbTest = new WriteableBitmap(bs);

            //heatmap = FindColoredDifference(convertedImage, PrevConvertedImage);
            //Bitmap b = BitmapFromWriteableBitmap(FindColoredDifference(convertedImage, PrevConvertedImage));
            heatmap.Lock();
            byte* bb = (byte*)heatmap.BackBuffer.ToPointer();
            byte* bbTest = (byte*)wbTest.BackBuffer.ToPointer();
            long L = (int)heatmap.Width * (int)heatmap.Height * 3;
            long arrCount = 0;
            long nPixels = (int)heatmap.Width * (int)heatmap.Height;
            byte[] pixel = new byte[nPixels*3];
            int[] REDS = new int[L];
            int[] GREENS = new int[L];
            int[] BLUES = new int[L];

            //Color color = new Color;
            Bitmap b = new Bitmap((int)heatmap.Width, (int)heatmap.Height, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            //return b;
            double max = double.MinValue, min = double.MaxValue;
            for (int bl = 0, g = 1, r = 2; bl < L; bl += 3, r += 3, g += 3)
            {
                bbTest[g] = bb[g];
                //int R, G, B;
                double delta = max - min;
                if (bbTest[g] > max && bbTest[g] < 40 * nPixels) max = bbTest[g];
                if (bbTest[g] < min) min = bbTest[g];
                pixel[g] = bbTest[g];
                //bb[g] = (int)((1.0 - bb[g] - min) / delta) * 130.0);
                //if (bb[g] > 231) bb[g] = 231;

                HsvToRgb(bbTest[g], 1, 1, out REDS[g], out GREENS[g], out BLUES[g]);
                
                //b.SetPixel(i, j, cc);
                //bbTest[g] = bb[g];
                //arrCount++;
                //Console.WriteLine($"arrCount = {arrCount}");
                //Console.WriteLine($"bl = {bl}, g = {g}, pixel[g] = {pixel[g]}, bb[g] = {bb[g]}");
            }
            var REDS_2D = Make2DArray(REDS, (int)heatmap.Height, (int)heatmap.Width);
            var GREENS_2D = Make2DArray(GREENS, (int)heatmap.Height, (int)heatmap.Width);
            var BLUES_2D = Make2DArray(BLUES, (int)heatmap.Height, (int)heatmap.Width);
            arrCount = 0;
            Console.WriteLine($"Массив pixel заполнен, длина = {pixel.Length}, ранг = {pixel.Rank}");
            //Console.WriteLine($"Исходный массив кадра, длина = {bb.Length}, ранг = {bb.Rank}");
            //Console.WriteLine($"Size of pixel = {sizeof(pixel)}");
            //int[,] a = new int[(int)heatmap.Width, (int)heatmap.Height];
            //Console.WriteLine($"Массив a[i,j] объявлнен, длина = {a.Length}, ранг = {a.Rank}");

            //for (int i = 0; i < (int)heatmap.Width; ++i)
            //    for (int j = 0; j < (int)heatmap.Height; ++j)
            //    {
            //        a[i, j] = pixel[j * (int)heatmap.Width + i];
            //        //Console.WriteLine($"i = {i}, j = {j}, a[i,j] = {a[i, j]}");
            //        //a[i, j] =heatmap[i,j];
            //        //arrCount++;
            //        //Console.WriteLine($"arrCount = {arrCount}");
            //    }
            //Console.WriteLine($"Массив a[i,j] заполнен, длина = {a.Length}, ранг = {a.Rank}");
            //arrCount = 0;
            //double max = double.MinValue, min = double.MaxValue;
            //for (int i = 0; i < (int)heatmap.Width; i++)
            //    for (int j = 0; j < (int)heatmap.Height; j++)
            //    {
            //        if (a[i, j] > max && a[i, j] < 40 * nPixels) max = a[i, j];
            //        if (a[i, j] < min) min = a[i, j];
            //        //Console.WriteLine($"i = {i}, j = {j}, a[i,j] = {a[i, j]}");
            //        //arrCount++;
            //        //Console.WriteLine($"arrCount = {arrCount}");
            //    }
            //double d = max - min;
            //Console.WriteLine($"Массив a[i,j] отнормирован, длина = {a.Length}, ранг = {a.Rank}");
            //Console.WriteLine($"d = {d}, max = {max}, min = {min}");
            //arrCount = 0;
            //for (int i = 0; i < (int)heatmap.Width; i++)
            //    for (int j = 0; j < (int)heatmap.Height; j++)
            //    {
            //        Color cc = Color.FromArgb(REDS_2D[j, i], GREENS_2D[j, i], BLUES_2D[j, i]);
            //        b.SetPixel(i, j, cc);
            //        //arrCount++;
            //        //Console.WriteLine($"arrCount = {arrCount}");
            //        //Console.WriteLine($"i = {i}, j = {j}, a[i, j] = {a[i, j]}, R = {R}, G = {G}, B = {B}");
            //    }
            //for (int i = 0; i < (int)heatmap.Height; i++)
            //{
            //    Marshal.Copy(data,
            //        i * (int)heatmap.Height,
            //        bmpData.Scan0 + i * bmpData.Stride,
            //        (int)heatmap.Width * 3);
            //}
            b = ByteToImage((int)heatmap.Width, (int)heatmap.Height, pixel);

            //Console.WriteLine($"Цвета выставлены!");
            //heatmap.Unlock();
            //Console.WriteLine("Метод отработал!");
            //b.Save(@"C:\MEDIA\test.bmp");

            bsOut = Convert(b);

            //return b;
            //return wbTest;
            return bsOut;
        }

        private Bitmap ByteToImage(int w, int h, byte[] pixels)
        {
            var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format24bppRgb);
            byte bpp = 3;
            var BoundsRect = new System.Drawing.Rectangle(0, 0, bmp.Width, bmp.Height);
            BitmapData bmpData = bmp.LockBits(BoundsRect,
                                            ImageLockMode.WriteOnly,
                                            bmp.PixelFormat);
            // copy line by line:
            for (int y = 0; y < h; y++)
                Marshal.Copy(pixels, y * w * bpp, bmpData.Scan0 + bmpData.Stride * y, w * bpp);
            bmp.UnlockBits(bmpData);

            return bmp;
        }

        private static T[,] Make2DArray<T>(T[] input, int height, int width)
        {
            T[,] output = new T[height, width];
            for (int i = 0; i < height; i++)
            {
                for (int j = 0; j < width; j++)
                {
                    output[i, j] = input[i * width + j];
                }
            }
            return output;
        }

        public BitmapSource Convert(System.Drawing.Bitmap bitmap)
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

        //public void DrawImage2FloatRectF(PaintEventArgs e)
        //{
        //    float x = 100.0F;
        //    float y = 100.0F;

        //    // Create rectangle for source image.
        //    RectangleF srcRect = new RectangleF(50.0F, 50.0F, 150.0F, 150.0F);
        //    GraphicsUnit units = GraphicsUnit.Pixel;

        //    // Draw image to screen.
        //    e.Graphics.DrawImage(b, x, y, srcRect, units);
        //}

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
                    !(bool)DrawDiffCheckBox.IsChecked ? "Preview" : ("White" + "_Coef" + (int)(AmplificationSlider.Value))
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
                    !(bool)DrawDiffCheckBox.IsChecked ? "Preview" : ("Fluo_" + "Green" + "_Coef" + (int)(AmplificationSlider.Value))
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
                    !(bool)DrawDiffCheckBox.IsChecked ? "Preview" : ("Fluo_" + "Red" + "_Coef" + (int)(AmplificationSlider.Value))
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
                    !(bool)DrawDiffCheckBox.IsChecked ? "Preview" : ("Fluo_" + "R2G" + "_Coef" + (int)(AmplificationSlider.Value) * additionalCoef)
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
                encoder.Frames.Add(BitmapFrame.Create(FindColoredDifference(convertedImage, PrevConvertedImage, 3)));
                DateTime d = DateTime.Now;
                string Filename = @"C:\MEDIA\" + String.Format("{0}_{1}_{2}_{3}_{4}_{5}_{6}_{7}.PNG",
                    d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Millisecond,
                    !(bool)DrawDiffCheckBox.IsChecked ? "Preview" : ("Fluo_" + "HeatmapDebug" + "_Coef" + (int)(AmplificationSlider.Value) * additionalCoef)
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
                encoder.Frames.Add(BitmapFrame.Create(Heatmap(FindColoredDifference(convertedImage, PrevConvertedImage, 0))));
                DateTime d = DateTime.Now;
                string Filename = @"C:\MEDIA\" + String.Format("{0}_{1}_{2}_{3}_{4}_{5}_{6}_{7}.PNG",
                    d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Millisecond,
                    !(bool)DrawDiffCheckBox.IsChecked ? "Preview" : ("Fluo_" + "HeatmapDebug" + "_Coef" + (int)(AmplificationSlider.Value) * additionalCoef)
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

            //if (pseudo)
            //{

            //DateTime d = DateTime.Now;
            //    string Filename = @"C:\MEDIA\" + String.Format("{0}_{1}_{2}_{3}_{4}_{5}_{6}_{7}.PNG",
            //        d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Millisecond,
            //        !(bool)DrawDiffCheckBox.IsChecked ? "Preview" : ("Pseudo " + ((bool)radioButtonGreen.IsChecked ? "green" : "red") + "_Coef" + (int)(AmplificationSlider.Value))
            //        );
            //    //Bitmap myBitmap = new Bitmap(@"C:\MEDIA\test.png");
            //    Bitmap myBitmap = Heatmap(FindColoredDifference(convertedImage, PrevConvertedImage, 0));
            //    myBitmap.Save(Filename, System.Drawing.Imaging.ImageFormat.Png);
            //    // Draw myBitmap to the screen.


            //}
        }
    }
}
