using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace ScreenshotQ
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly string _outputFolder;

        public MainWindow()
        {
            InitializeComponent();

            _outputFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "ScreenshotQ");

            Directory.CreateDirectory(_outputFolder);
        }

        private async void ScreenshotButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                ScreenshotButton.IsEnabled = false;
                StatusText.Text = "Capturing screen...";

                WindowState = WindowState.Minimized;
                await Task.Delay(220);

                (BitmapSource frozenImage, Rect virtualBounds) = CaptureVirtualScreenImage();
                var editor = new SnipEditorWindow(frozenImage, _outputFolder, virtualBounds);
                bool? result = editor.ShowDialog();

                WindowState = WindowState.Normal;
                Activate();

                if (result == true && !string.IsNullOrWhiteSpace(editor.SavedFilePath))
                {
                    StatusText.Text = "Saved: " + editor.SavedFilePath;
                }
                else
                {
                    StatusText.Text = "Capture canceled.";
                }
            }
            catch (Exception ex)
            {
                WindowState = WindowState.Normal;
                Activate();
                StatusText.Text = "Error: " + ex.Message;
            }
            finally
            {
                ScreenshotButton.IsEnabled = true;
            }
        }

        private void OpenFolderButton_Click(object sender, RoutedEventArgs e)
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = _outputFolder,
                UseShellExecute = true
            });

            StatusText.Text = "Opened folder: " + _outputFolder;
        }

        private (BitmapSource Image, Rect Bounds) CaptureVirtualScreenImage()
        {
            int left = (int)SystemParameters.VirtualScreenLeft;
            int top = (int)SystemParameters.VirtualScreenTop;
            int width = (int)SystemParameters.VirtualScreenWidth;
            int height = (int)SystemParameters.VirtualScreenHeight;

            using var bitmap = new Bitmap(width, height);
            using (var graphics = Graphics.FromImage(bitmap))
            {
                graphics.CopyFromScreen(left, top, 0, 0, new System.Drawing.Size(width, height));
            }

            IntPtr hBitmap = bitmap.GetHbitmap();
            try
            {
                BitmapSource source = Imaging.CreateBitmapSourceFromHBitmap(
                    hBitmap,
                    IntPtr.Zero,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());

                source.Freeze();
                return (source, new Rect(left, top, width, height));
            }
            finally
            {
                DeleteObject(hBitmap);
            }
        }

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);
    }
}