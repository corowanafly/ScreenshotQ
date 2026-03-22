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

        private const double MinSelectionSize = 12;

        private readonly string _outputFolder;
        private readonly double _dpiScaleX;
        private readonly double _dpiScaleY;
        private readonly double _surfaceWidth;
        private readonly double _surfaceHeight;
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

            _surfaceWidth = screenshot.PixelWidth / _dpiScaleX;
            _surfaceHeight = screenshot.PixelHeight / _dpiScaleY;

            FrozenImage.Source = screenshot;
            FrozenImage.Width = _surfaceWidth;
            FrozenImage.Height = _surfaceHeight;

            SelectedPreview.Source = screenshot;
            SelectedPreview.Width = _surfaceWidth;
            SelectedPreview.Height = _surfaceHeight;

            OverlayCanvas.Width = _surfaceWidth;
            OverlayCanvas.Height = _surfaceHeight;

            SelectionRectangle.Width = 0;
            SelectionRectangle.Height = 0;
            SelectionRectangle.Visibility = Visibility.Collapsed;
            SelectedPreview.Visibility = Visibility.Collapsed;
            ToolPanel.Visibility = Visibility.Collapsed;
            SetResizeThumbsVisibility(Visibility.Collapsed);

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

            if (_currentTool != EditorTool.Select && !_hasSelection)
            {
                HintText.Text = "Select an area first.";
                return;
            }

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
                ToolPanel.Visibility = Visibility.Collapsed;
                SetResizeThumbsVisibility(Visibility.Collapsed);
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
                if (dragRect.Width >= MinSelectionSize && dragRect.Height >= MinSelectionSize)
                {
                    _selectionRect = dragRect;
                    _hasSelection = true;
                    ApplySelectedAreaVisual(dragRect);
                    PositionToolPanelNearSelection(dragRect);
                    HintText.Text = string.Format(CultureInfo.InvariantCulture, "Selected area: {0:0} x {1:0}", _selectionRect.Width, _selectionRect.Height);
                }
                else
                {
                    _hasSelection = false;
                    SelectionRectangle.Visibility = Visibility.Collapsed;
                    SelectedPreview.Visibility = Visibility.Collapsed;
                    ToolPanel.Visibility = Visibility.Collapsed;
                    SetResizeThumbsVisibility(Visibility.Collapsed);
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

        private void ApplySelectedAreaVisual(Rect selection)
        {
            _selectionRect = NormalizeRect(selection.TopLeft, selection.BottomRight);
            SelectionRectangle.Visibility = Visibility.Visible;
            SetSelectionVisual(_selectionRect);
            SelectedPreview.Clip = new RectangleGeometry(_selectionRect);
            SelectedPreview.Visibility = Visibility.Visible;
            UpdateResizeThumbPositions(_selectionRect);
            SetResizeThumbsVisibility(Visibility.Visible);
        }

        private void PositionToolPanelNearSelection(Rect selection)
        {
            ToolPanel.Visibility = Visibility.Visible;
            ToolPanel.UpdateLayout();

            const double gap = 10;
            const double border = 10;

            double panelWidth = Math.Max(100, ToolPanel.ActualWidth);
            double panelHeight = Math.Max(40, ToolPanel.ActualHeight);

            bool isFullScreenSelection = selection.Left <= 1 && selection.Top <= 1 &&
                                         Math.Abs(selection.Width - _surfaceWidth) <= 2 &&
                                         Math.Abs(selection.Height - _surfaceHeight) <= 2;

            double x;
            double y;

            if (isFullScreenSelection)
            {
                x = Clamp(selection.Left + (selection.Width - panelWidth) / 2, border, _surfaceWidth - panelWidth - border);
                y = Clamp(selection.Top + gap, border, _surfaceHeight - panelHeight - border);
            }
            else
            {
                x = Clamp(selection.Left + (selection.Width - panelWidth) / 2, border, _surfaceWidth - panelWidth - border);
                double topY = selection.Top - panelHeight - gap;
                double bottomY = selection.Bottom + gap;

                bool topFits = topY >= border;
                bool bottomFits = bottomY + panelHeight <= _surfaceHeight - border;

                if (topFits)
                {
                    y = topY;
                }
                else if (bottomFits)
                {
                    y = bottomY;
                }
                else
                {
                    y = Clamp(bottomY, border, _surfaceHeight - panelHeight - border);
                }
            }

            ToolPanel.Margin = new Thickness(x, y, 0, 0);
        }

        private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (!_hasSelection || sender is not Thumb thumb || thumb.Tag is not string tag)
            {
                return;
            }

            double left = _selectionRect.Left;
            double top = _selectionRect.Top;
            double right = _selectionRect.Right;
            double bottom = _selectionRect.Bottom;

            if (tag.Contains("Left", StringComparison.Ordinal))
            {
                left = Clamp(left + e.HorizontalChange, 0, right - MinSelectionSize);
            }

            if (tag.Contains("Right", StringComparison.Ordinal))
            {
                right = Clamp(right + e.HorizontalChange, left + MinSelectionSize, _surfaceWidth);
            }

            if (tag.Contains("Top", StringComparison.Ordinal))
            {
                top = Clamp(top + e.VerticalChange, 0, bottom - MinSelectionSize);
            }

            if (tag.Contains("Bottom", StringComparison.Ordinal))
            {
                bottom = Clamp(bottom + e.VerticalChange, top + MinSelectionSize, _surfaceHeight);
            }

            _selectionRect = new Rect(left, top, right - left, bottom - top);
            ApplySelectedAreaVisual(_selectionRect);
            PositionToolPanelNearSelection(_selectionRect);
            HintText.Text = string.Format(CultureInfo.InvariantCulture, "Selected area: {0:0} x {1:0}", _selectionRect.Width, _selectionRect.Height);
        }

        private void UpdateResizeThumbPositions(Rect selection)
        {
            double left = selection.Left;
            double top = selection.Top;
            double right = selection.Right;
            double bottom = selection.Bottom;
            double midX = left + selection.Width / 2;
            double midY = top + selection.Height / 2;

            SetThumbPosition(TopLeftThumb, left, top);
            SetThumbPosition(TopThumb, midX, top);
            SetThumbPosition(TopRightThumb, right, top);
            SetThumbPosition(RightThumb, right, midY);
            SetThumbPosition(BottomRightThumb, right, bottom);
            SetThumbPosition(BottomThumb, midX, bottom);
            SetThumbPosition(BottomLeftThumb, left, bottom);
            SetThumbPosition(LeftThumb, left, midY);
        }

        private static void SetThumbPosition(Thumb thumb, double centerX, double centerY)
        {
            Canvas.SetLeft(thumb, centerX - (thumb.Width / 2));
            Canvas.SetTop(thumb, centerY - (thumb.Height / 2));
        }

        private void SetResizeThumbsVisibility(Visibility visibility)
        {
            TopLeftThumb.Visibility = visibility;
            TopThumb.Visibility = visibility;
            TopRightThumb.Visibility = visibility;
            RightThumb.Visibility = visibility;
            BottomRightThumb.Visibility = visibility;
            BottomThumb.Visibility = visibility;
            BottomLeftThumb.Visibility = visibility;
            LeftThumb.Visibility = visibility;
        }

        private static double Clamp(double value, double min, double max)
        {
            if (max < min)
            {
                return min;
            }

            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
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
                if (!ReferenceEquals(OverlayCanvas.Children[i], SelectionRectangle) &&
                    OverlayCanvas.Children[i] is not Thumb)
                {
                    OverlayCanvas.Children.RemoveAt(i);
                }
            }

            SelectionRectangle.Visibility = Visibility.Collapsed;
            SelectedPreview.Visibility = Visibility.Collapsed;
            SelectedPreview.Clip = null;
            ToolPanel.Visibility = Visibility.Collapsed;
            SetResizeThumbsVisibility(Visibility.Collapsed);
            _hasSelection = false;
            _numberCounter = 1;
            HintText.Text = "Cleared. Drag to select an area again.";
            _currentTool = EditorTool.Select;
            SelectToolButton.IsChecked = true;
            RectToolButton.IsChecked = false;
            EllipseToolButton.IsChecked = false;
            ArrowToolButton.IsChecked = false;
            TextToolButton.IsChecked = false;
            NumberToolButton.IsChecked = false;
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
            ToolPanel.Visibility = Visibility.Collapsed;
            SetResizeThumbsVisibility(Visibility.Collapsed);

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
