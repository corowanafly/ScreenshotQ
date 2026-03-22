using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using Point = System.Windows.Point;
using ShapePath = System.Windows.Shapes.Path;

namespace ScreenshotQ
{
    public partial class SnipEditorWindow : Window
    {
        private enum EditorTool
        {
            Select,
            Rectangle,
            Ellipse,
            Arrow,
            Text,
            Number
        }

        private readonly string _outputFolder;
        private readonly double _dpiScaleX;
        private readonly double _dpiScaleY;
        private EditorTool _currentTool = EditorTool.Select;
        private Point _dragStart;
        private bool _isDragging;
        private Shape? _activeShape;
        private Rect _selectionRect;
        private bool _hasSelection;
        private int _numberCounter = 1;

        public string? SavedFilePath { get; private set; }

        public SnipEditorWindow(BitmapSource screenshot, string outputFolder, Rect virtualBounds)
        {
            InitializeComponent();

            _outputFolder = outputFolder;
            Directory.CreateDirectory(_outputFolder);

            DpiScale dpi = VisualTreeHelper.GetDpi(Application.Current.MainWindow ?? this);
            _dpiScaleX = dpi.DpiScaleX;
            _dpiScaleY = dpi.DpiScaleY;

            Left = virtualBounds.Left / _dpiScaleX;
            Top = virtualBounds.Top / _dpiScaleY;
            Width = virtualBounds.Width / _dpiScaleX;
            Height = virtualBounds.Height / _dpiScaleY;

            double surfaceWidth = screenshot.PixelWidth / _dpiScaleX;
            double surfaceHeight = screenshot.PixelHeight / _dpiScaleY;

            FrozenImage.Source = screenshot;
            FrozenImage.Width = surfaceWidth;
            FrozenImage.Height = surfaceHeight;

            OverlayCanvas.Width = surfaceWidth;
            OverlayCanvas.Height = surfaceHeight;
            SelectionRectangle.Width = 0;
            SelectionRectangle.Height = 0;

            HintText.Text = "Drag to select an area. Press Esc to cancel.";
        }

        private void ToolButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton clicked)
            {
                return;
            }

            SelectToolButton.IsChecked = false;
            RectToolButton.IsChecked = false;
            EllipseToolButton.IsChecked = false;
            ArrowToolButton.IsChecked = false;
            TextToolButton.IsChecked = false;
            NumberToolButton.IsChecked = false;
            clicked.IsChecked = true;

            if (clicked == SelectToolButton)
            {
                _currentTool = EditorTool.Select;
                HintText.Text = "Drag to define screenshot area.";
            }
            else if (clicked == RectToolButton)
            {
                _currentTool = EditorTool.Rectangle;
                HintText.Text = "Drag to draw rectangle.";
            }
            else if (clicked == EllipseToolButton)
            {
                _currentTool = EditorTool.Ellipse;
                HintText.Text = "Drag to draw ellipse.";
            }
            else if (clicked == ArrowToolButton)
            {
                _currentTool = EditorTool.Arrow;
                HintText.Text = "Drag to draw arrow.";
            }
            else if (clicked == TextToolButton)
            {
                _currentTool = EditorTool.Text;
                HintText.Text = "Click on image to place text.";
            }
            else if (clicked == NumberToolButton)
            {
                _currentTool = EditorTool.Number;
                HintText.Text = "Click to place a numbered marker.";
            }
        }

        private void OverlayCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Point point = e.GetPosition(OverlayCanvas);

            if (_currentTool == EditorTool.Text)
            {
                InsertText(point);
                return;
            }

            if (_currentTool == EditorTool.Number)
            {
                InsertNumber(point);
                return;
            }

            _dragStart = point;
            _isDragging = true;
            OverlayCanvas.CaptureMouse();

            if (_currentTool == EditorTool.Select)
            {
                SelectionRectangle.Visibility = Visibility.Visible;
                SetSelectionVisual(new Rect(point, point));
                return;
            }

            _activeShape = CreateShapeForTool(_currentTool);
            if (_activeShape is not null)
            {
                OverlayCanvas.Children.Add(_activeShape);
                UpdateActiveShape(point);
            }
        }

        private void OverlayCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging)
            {
                return;
            }

            Point point = e.GetPosition(OverlayCanvas);

            if (_currentTool == EditorTool.Select)
            {
                SetSelectionVisual(new Rect(_dragStart, point));
                return;
            }

            UpdateActiveShape(point);
        }

        private void OverlayCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging)
            {
                return;
            }

            _isDragging = false;
            OverlayCanvas.ReleaseMouseCapture();

            Point endPoint = e.GetPosition(OverlayCanvas);
            Rect dragRect = NormalizeRect(_dragStart, endPoint);

            if (_currentTool == EditorTool.Select)
            {
                if (dragRect.Width >= 6 && dragRect.Height >= 6)
                {
                    _selectionRect = dragRect;
                    _hasSelection = true;
                    HintText.Text = string.Format(CultureInfo.InvariantCulture, "Selected area: {0:0} x {1:0}", _selectionRect.Width, _selectionRect.Height);
                }
                else
                {
                    _hasSelection = false;
                    SelectionRectangle.Visibility = Visibility.Collapsed;
                    HintText.Text = "Selection is too small. Drag again.";
                }
            }
            else if (_activeShape is not null)
            {
                if (dragRect.Width < 3 && dragRect.Height < 3)
                {
                    OverlayCanvas.Children.Remove(_activeShape);
                }

                _activeShape = null;
            }
        }

        private Shape? CreateShapeForTool(EditorTool tool)
        {
            return tool switch
            {
                EditorTool.Rectangle => new Rectangle
                {
                    Stroke = new SolidColorBrush(Color.FromRgb(244, 63, 94)),
                    Fill = new SolidColorBrush(Color.FromArgb(28, 244, 63, 94)),
                    StrokeThickness = 3,
                    IsHitTestVisible = false
                },
                EditorTool.Ellipse => new Ellipse
                {
                    Stroke = new SolidColorBrush(Color.FromRgb(16, 185, 129)),
                    Fill = new SolidColorBrush(Color.FromArgb(28, 16, 185, 129)),
                    StrokeThickness = 3,
                    IsHitTestVisible = false
                },
                EditorTool.Arrow => new ShapePath
                {
                    Stroke = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
                    StrokeThickness = 3,
                    Fill = Brushes.Transparent,
                    IsHitTestVisible = false
                },
                _ => null
            };
        }

        private void UpdateActiveShape(Point current)
        {
            if (_activeShape is null)
            {
                return;
            }

            Rect rect = NormalizeRect(_dragStart, current);

            if (_activeShape is Rectangle || _activeShape is Ellipse)
            {
                if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                {
                    double size = Math.Max(rect.Width, rect.Height);
                    double left = current.X >= _dragStart.X ? _dragStart.X : _dragStart.X - size;
                    double top = current.Y >= _dragStart.Y ? _dragStart.Y : _dragStart.Y - size;
                    rect = new Rect(left, top, size, size);
                }

                Canvas.SetLeft(_activeShape, rect.Left);
                Canvas.SetTop(_activeShape, rect.Top);
                _activeShape.Width = rect.Width;
                _activeShape.Height = rect.Height;
                return;
            }

            if (_activeShape is ShapePath path)
            {
                path.Data = BuildArrowGeometry(_dragStart, current);
            }
        }

        private static Geometry BuildArrowGeometry(Point start, Point end)
        {
            Vector direction = end - start;
            if (direction.Length < 1)
            {
                direction = new Vector(1, 0);
            }

            direction.Normalize();

            double headLength = 16;
            double headAngle = 30 * Math.PI / 180;

            Vector back = -direction;
            Vector left = RotateVector(back, headAngle) * headLength;
            Vector right = RotateVector(back, -headAngle) * headLength;

            Point headLeft = end + left;
            Point headRight = end + right;

            StreamGeometry geometry = new();
            using (StreamGeometryContext ctx = geometry.Open())
            {
                ctx.BeginFigure(start, false, false);
                ctx.LineTo(end, true, true);

                ctx.BeginFigure(headLeft, false, false);
                ctx.LineTo(end, true, true);
                ctx.LineTo(headRight, true, true);
            }

            geometry.Freeze();
            return geometry;
        }

        private static Vector RotateVector(Vector vector, double angle)
        {
            double cos = Math.Cos(angle);
            double sin = Math.Sin(angle);
            return new Vector(
                vector.X * cos - vector.Y * sin,
                vector.X * sin + vector.Y * cos);
        }

        private void InsertText(Point point)
        {
            string? text = PromptText();
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            Border textBadge = new()
            {
                Background = new SolidColorBrush(Color.FromArgb(220, 15, 23, 42)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 4, 8, 4),
                Child = new TextBlock
                {
                    Text = text,
                    Foreground = Brushes.White,
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold
                },
                IsHitTestVisible = false
            };

            OverlayCanvas.Children.Add(textBadge);
            Canvas.SetLeft(textBadge, point.X);
            Canvas.SetTop(textBadge, point.Y);
        }

        private void InsertNumber(Point point)
        {
            Grid marker = new()
            {
                Width = 28,
                Height = 28,
                IsHitTestVisible = false
            };

            marker.Children.Add(new Ellipse
            {
                Fill = new SolidColorBrush(Color.FromRgb(245, 158, 11)),
                Stroke = new SolidColorBrush(Color.FromRgb(180, 83, 9)),
                StrokeThickness = 2
            });

            marker.Children.Add(new TextBlock
            {
                Text = _numberCounter.ToString(CultureInfo.InvariantCulture),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            });

            _numberCounter++;
            OverlayCanvas.Children.Add(marker);
            Canvas.SetLeft(marker, point.X - 14);
            Canvas.SetTop(marker, point.Y - 14);
        }

        private string? PromptText()
        {
            Window prompt = new()
            {
                Title = "Insert Text",
                Width = 360,
                Height = 170,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow,
                Background = Brushes.White
            };

            Grid grid = new();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.Margin = new Thickness(16);

            TextBlock label = new() { Text = "Text content:" };
            TextBox input = new() { Margin = new Thickness(0, 8, 0, 14), MinWidth = 300 };
            Button okButton = new() { Content = "OK", Width = 80, IsDefault = true };

            string? result = null;
            okButton.Click += (_, _) =>
            {
                result = input.Text;
                prompt.DialogResult = true;
                prompt.Close();
            };

            Grid.SetRow(label, 0);
            Grid.SetRow(input, 1);
            Grid.SetRow(okButton, 2);

            grid.Children.Add(label);
            grid.Children.Add(input);
            grid.Children.Add(okButton);

            prompt.Content = grid;
            prompt.ShowDialog();
            return result;
        }

        private void SetSelectionVisual(Rect rect)
        {
            Rect normalized = NormalizeRect(rect.TopLeft, rect.BottomRight);
            SelectionRectangle.Visibility = Visibility.Visible;
            SelectionRectangle.Width = normalized.Width;
            SelectionRectangle.Height = normalized.Height;
            Canvas.SetLeft(SelectionRectangle, normalized.Left);
            Canvas.SetTop(SelectionRectangle, normalized.Top);
        }

        private static Rect NormalizeRect(Point start, Point end)
        {
            double left = Math.Min(start.X, end.X);
            double top = Math.Min(start.Y, end.Y);
            double width = Math.Abs(end.X - start.X);
            double height = Math.Abs(end.Y - start.Y);
            return new Rect(left, top, width, height);
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            for (int i = OverlayCanvas.Children.Count - 1; i >= 0; i--)
            {
                if (!ReferenceEquals(OverlayCanvas.Children[i], SelectionRectangle))
                {
                    OverlayCanvas.Children.RemoveAt(i);
                }
            }

            SelectionRectangle.Visibility = Visibility.Collapsed;
            _hasSelection = false;
            _numberCounter = 1;
            HintText.Text = "Cleared. Drag to select an area again.";
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!_hasSelection)
                {
                    _selectionRect = new Rect(0, 0, OverlayCanvas.Width, OverlayCanvas.Height);
                }

                SaveSelectionToFile(_selectionRect);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                HintText.Text = "Save failed: " + ex.Message;
            }
        }

        private void SaveSelectionToFile(Rect selection)
        {
            SelectionRectangle.Visibility = Visibility.Collapsed;

            int totalWidth = (int)Math.Round(OverlayCanvas.Width * _dpiScaleX);
            int totalHeight = (int)Math.Round(OverlayCanvas.Height * _dpiScaleY);

            RenderTargetBitmap rendered = new(
                totalWidth,
                totalHeight,
                96 * _dpiScaleX,
                96 * _dpiScaleY,
                PixelFormats.Pbgra32);
            rendered.Render(SurfaceRoot);

            int x = (int)Math.Max(0, Math.Floor(selection.X * _dpiScaleX));
            int y = (int)Math.Max(0, Math.Floor(selection.Y * _dpiScaleY));
            int width = (int)Math.Min(totalWidth - x, Math.Ceiling(selection.Width * _dpiScaleX));
            int height = (int)Math.Min(totalHeight - y, Math.Ceiling(selection.Height * _dpiScaleY));

            if (width <= 1 || height <= 1)
            {
                throw new InvalidOperationException("Selection is too small.");
            }

            CroppedBitmap cropped = new(rendered, new Int32Rect(x, y, width, height));

            string fileName = DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_edited.png";
            string filePath = System.IO.Path.Combine(_outputFolder, fileName);

            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(cropped));

            using FileStream stream = new(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            encoder.Save(stream);

            SavedFilePath = filePath;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                DialogResult = false;
                Close();
            }
        }
    }
}

