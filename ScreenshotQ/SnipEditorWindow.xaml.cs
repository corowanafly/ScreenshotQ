using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Color = System.Windows.Media.Color;
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
        private const double MinShapeSize = 10;
        private const double MinTextWidth = 100;
        private const double MinTextHeight = 36;
        private const double DefaultStrokeThickness = 4;
        private static readonly Color DefaultAnnotationColor = Color.FromRgb(255, 23, 68);

        private readonly string _outputFolder;
        private readonly double _dpiScaleX;
        private readonly double _dpiScaleY;
        private readonly double _surfaceWidth;
        private readonly double _surfaceHeight;
        private readonly HashSet<Shape> _editableShapes = new();
        private readonly Dictionary<ShapePath, (Point Start, Point End)> _arrowAnchors = new();
        private EditorTool _currentTool = EditorTool.Select;
        private Point _dragStart;
        private bool _isDragging;
        private bool _isMovingSelectedShape;
        private Point _shapeMoveStart;
        private (Point Start, Point End)? _movingArrowInitialAnchors;
        private Shape? _activeShape;
        private Shape? _selectedShape;
        private Rect _selectionRect;
        private bool _hasSelection;
        private int _numberCounter = 1;
        private readonly HashSet<Border> _editableTextBorders = new();
        private readonly Stack<Action> _undoActions = new();
        private readonly Dictionary<string, (Key Key, ModifierKeys Modifiers)> _shortcutGestures = new();
        private Border? _selectedTextBorder;
        private bool _isMovingTextBorder;
        private Point _textBorderMoveStart;
        private Color _currentAnnotationColor = DefaultAnnotationColor;
        private double _currentStrokeThickness = DefaultStrokeThickness;
        private bool _shortcutsEnabled;

        public string? SavedFilePath { get; private set; }

        public SnipEditorWindow(BitmapSource screenshot, string outputFolder, Rect virtualBounds, Dictionary<string, (Key Key, ModifierKeys Modifiers)> shortcuts)
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
            SetShapeResizeThumbsVisibility(Visibility.Collapsed);
            SetArrowEndpointThumbsVisibility(Visibility.Collapsed);
            SetTextResizeThumbsVisibility(Visibility.Collapsed);
            UpdateCurrentColorSwatch();
            UpdateCurrentStrokePreview();
            foreach (var kvp in shortcuts)
                _shortcutGestures[kvp.Key] = kvp.Value;
            _shortcutsEnabled = false;

            Loaded += async (_, _) =>
            {
                // Prevent key carry-over (for example from the trigger button) from firing shortcut actions.
                await Task.Delay(1200);
                _shortcutsEnabled = true;
            };

            HintText.Text = "Drag to select an area. Press Esc to cancel.";
        }

        private void ToolButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton clicked)
            {
                return;
            }

            if (clicked == RectToolButton)
            {
                ApplyToolSelection(EditorTool.Rectangle);
            }
            else if (clicked == EllipseToolButton)
            {
                ApplyToolSelection(EditorTool.Ellipse);
            }
            else if (clicked == ArrowToolButton)
            {
                ApplyToolSelection(EditorTool.Arrow);
            }
            else if (clicked == TextToolButton)
            {
                ApplyToolSelection(EditorTool.Text);
            }
            else if (clicked == NumberToolButton)
            {
                ApplyToolSelection(EditorTool.Number);
            }
        }

        private void ApplyToolSelection(EditorTool tool)
        {
            _currentTool = tool;

            RectToolButton.IsChecked = tool == EditorTool.Rectangle;
            EllipseToolButton.IsChecked = tool == EditorTool.Ellipse;
            ArrowToolButton.IsChecked = tool == EditorTool.Arrow;
            TextToolButton.IsChecked = tool == EditorTool.Text;
            NumberToolButton.IsChecked = tool == EditorTool.Number;

            HintText.Text = tool switch
            {
                EditorTool.Rectangle => "Drag to draw rectangle.",
                EditorTool.Ellipse => "Drag to draw ellipse.",
                EditorTool.Arrow => "Drag to draw arrow.",
                EditorTool.Text => "Click on image to place text.",
                EditorTool.Number => "Click to place a numbered marker.",
                _ => HintText.Text,
            };
        }

        private void OverlayCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Point point = e.GetPosition(OverlayCanvas);

            Border? hitTextBorder = HitTestTextBorder(point);
            if (hitTextBorder is not null)
            {
                SelectTextBorder(hitTextBorder);
                ClearSelectedShape();

                if (hitTextBorder.Child is TextBox activeTextBox && !activeTextBox.IsReadOnly)
                {
                    return;
                }

                if (e.ClickCount >= 2)
                {
                    BeginEditTextBorder(hitTextBorder);
                }
                else
                {
                    _isMovingTextBorder = true;
                    _textBorderMoveStart = point;
                    OverlayCanvas.CaptureMouse();
                }
                return;
            }

            ClearSelectedTextBorder();

            if (_currentTool != EditorTool.Select && !_hasSelection)
            {
                HintText.Text = "Select an area first.";
                return;
            }

            if (_selectedShape is not null && IsPointInsideShape(_selectedShape, point))
            {
                _isMovingSelectedShape = true;
                _shapeMoveStart = point;
                _movingArrowInitialAnchors = null;
                if (_selectedShape is ShapePath arrow && _arrowAnchors.TryGetValue(arrow, out (Point Start, Point End) anchors))
                {
                    _movingArrowInitialAnchors = anchors;
                }

                OverlayCanvas.CaptureMouse();
                HintText.Text = "Drag to move selected shape.";
                return;
            }

            if (TryFindEditableShape(point, out Shape hitShape))
            {
                SelectShape(hitShape);
                return;
            }

            ClearSelectedShape();

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
            Point point = e.GetPosition(OverlayCanvas);

            if (_isMovingTextBorder)
            {
                if (_selectedTextBorder is null)
                {
                    _isMovingTextBorder = false;
                    return;
                }

                Vector delta = point - _textBorderMoveStart;
                MoveTextBorder(delta);
                _textBorderMoveStart = point;
                return;
            }

            if (_isMovingSelectedShape)
            {
                if (_selectedShape is null)
                {
                    _isMovingSelectedShape = false;
                    return;
                }

                Vector delta = point - _shapeMoveStart;
                MoveSelectedShape(delta);
                _shapeMoveStart = point;
                return;
            }

            if (!_isDragging)
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    return;
                }

                if (TryFindEditableShape(point, out _) || HitTestTextBorder(point) is not null)
                {
                    OverlayCanvas.Cursor = Cursors.SizeAll;
                }
                else if (_selectedShape is not null && IsPointInsideShape(_selectedShape, point))
                {
                    OverlayCanvas.Cursor = Cursors.SizeAll;
                }
                else
                {
                    OverlayCanvas.Cursor = Cursors.Cross;
                }

                return;
            }

            if (_currentTool == EditorTool.Select)
            {
                SetSelectionVisual(new Rect(_dragStart, point));
                return;
            }

            UpdateActiveShape(point);
        }

        private void OverlayCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isMovingTextBorder)
            {
                _isMovingTextBorder = false;
                OverlayCanvas.ReleaseMouseCapture();
                HintText.Text = "Text moved.";
                return;
            }

            if (_isMovingSelectedShape)
            {
                _isMovingSelectedShape = false;
                _movingArrowInitialAnchors = null;
                OverlayCanvas.ReleaseMouseCapture();
                HintText.Text = "Shape moved.";
                return;
            }

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
                    HintText.Text = "Selection ready. Choose a tool and draw.";
                }
                else
                {
                    _hasSelection = false;
                    SelectionRectangle.Visibility = Visibility.Collapsed;
                    SelectedPreview.Visibility = Visibility.Collapsed;
                    ToolPanel.Visibility = Visibility.Collapsed;
                    SetResizeThumbsVisibility(Visibility.Collapsed);
                    ClearSelectedShape();
                    HintText.Text = "Selection is too small. Drag again.";
                }
            }
            else if (_activeShape is not null)
            {
                if (dragRect.Width < 3 && dragRect.Height < 3)
                {
                    OverlayCanvas.Children.Remove(_activeShape);
                }
                else
                {
                    Shape createdShape = _activeShape;
                    _editableShapes.Add(createdShape);

                    if (createdShape is ShapePath arrow)
                    {
                        _arrowAnchors[arrow] = (_dragStart, endPoint);
                    }

                    RegisterUndo(() =>
                    {
                        if (ReferenceEquals(_selectedShape, createdShape))
                        {
                            ClearSelectedShape();
                        }

                        _editableShapes.Remove(createdShape);
                        if (createdShape is ShapePath createdArrow)
                        {
                            _arrowAnchors.Remove(createdArrow);
                        }

                        OverlayCanvas.Children.Remove(createdShape);
                    });

                    HintText.Text = "Shape created. Click its edge to resize or select.";
                }

                _activeShape = null;
            }
        }

        private Shape? CreateShapeForTool(EditorTool tool)
        {
            Brush strokeBrush = new SolidColorBrush(_currentAnnotationColor);

            return tool switch
            {
                EditorTool.Rectangle => new Rectangle
                {
                    Stroke = strokeBrush,
                    Fill = Brushes.Transparent,
                    StrokeThickness = _currentStrokeThickness,
                    IsHitTestVisible = true
                },
                EditorTool.Ellipse => new Ellipse
                {
                    Stroke = strokeBrush,
                    Fill = Brushes.Transparent,
                    StrokeThickness = _currentStrokeThickness,
                    IsHitTestVisible = true
                },
                EditorTool.Arrow => new ShapePath
                {
                    Stroke = strokeBrush,
                    StrokeThickness = _currentStrokeThickness,
                    Fill = Brushes.Transparent,
                    IsHitTestVisible = true
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
            TextBox textBox = new()
            {
                Foreground = new SolidColorBrush(_currentAnnotationColor),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                MinWidth = MinTextWidth - 16,
                MinHeight = MinTextHeight - 8,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                CaretBrush = Brushes.White,
                Cursor = Cursors.IBeam,
                Text = string.Empty,
            };

            Border container = new()
            {
                Background = new SolidColorBrush(Color.FromArgb(220, 15, 23, 42)),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 4, 8, 4),
                Child = textBox,
                IsHitTestVisible = true,
                Width = 240,
                MinHeight = MinTextHeight,
            };

            Panel.SetZIndex(container, 500);
            OverlayCanvas.Children.Add(container);
            Canvas.SetLeft(container, point.X);
            Canvas.SetTop(container, point.Y);
            _editableTextBorders.Add(container);
            SelectTextBorder(container);

            RegisterUndo(() =>
            {
                if (ReferenceEquals(_selectedTextBorder, container))
                {
                    ClearSelectedTextBorder();
                }

                OverlayCanvas.Children.Remove(container);
                _editableTextBorders.Remove(container);
            });

            textBox.LostFocus += (_, _) => CommitTextBorder(container);
            textBox.TextChanged += (_, _) => AutoSizeTextBorder(container);
            textBox.KeyDown += (_, ke) =>
            {
                if (ke.Key == Key.Escape || (ke.Key == Key.Enter && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)))
                {
                    CommitTextBorder(container);
                    ke.Handled = true;
                }
            };

            container.UpdateLayout();
            AutoSizeTextBorder(container);
            textBox.Focus();
            HintText.Text = "Type text. Box auto-expands by lines. Ctrl+Enter or click outside to finish.";
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
                Fill = new SolidColorBrush(_currentAnnotationColor),
                Stroke = new SolidColorBrush(DarkenColor(_currentAnnotationColor, 0.68)),
                StrokeThickness = Math.Max(2, _currentStrokeThickness - 1)
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

            RegisterUndo(() =>
            {
                OverlayCanvas.Children.Remove(marker);
                _numberCounter = Math.Max(1, _numberCounter - 1);
            });
        }

        private void CommitTextBorder(Border container)
        {
            if (container.Child is not TextBox textBox)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(textBox.Text))
            {
                OverlayCanvas.Children.Remove(container);
                _editableTextBorders.Remove(container);
                if (ReferenceEquals(_selectedTextBorder, container))
                {
                    ClearSelectedTextBorder();
                }
            }
            else
            {
                textBox.IsReadOnly = true;
                textBox.IsHitTestVisible = false;
                textBox.Focusable = false;
                AutoSizeTextBorder(container);
                HintText.Text = "Text placed. Double-click to edit, drag to move, Delete to remove.";
            }
        }

        private void BeginEditTextBorder(Border container)
        {
            if (container.Child is not TextBox textBox)
            {
                return;
            }

            textBox.IsReadOnly = false;
            textBox.IsHitTestVisible = true;
            textBox.Focusable = true;
            textBox.Focus();
            textBox.CaretIndex = textBox.Text.Length;
            HintText.Text = "Editing text. Box auto-expands by lines. Ctrl+Enter or click outside to finish.";
        }

        private Border? HitTestTextBorder(Point point)
        {
            for (int i = OverlayCanvas.Children.Count - 1; i >= 0; i--)
            {
                if (OverlayCanvas.Children[i] is not Border border || !_editableTextBorders.Contains(border))
                {
                    continue;
                }

                double left = Canvas.GetLeft(border);
                double top = Canvas.GetTop(border);
                if (double.IsNaN(left) || double.IsNaN(top))
                {
                    continue;
                }

                if (new Rect(left, top, border.ActualWidth, border.ActualHeight).Contains(point))
                {
                    return border;
                }
            }

            return null;
        }

        private void MoveTextBorder(Vector delta)
        {
            if (_selectedTextBorder is null)
            {
                return;
            }

            double left = Canvas.GetLeft(_selectedTextBorder);
            double top = Canvas.GetTop(_selectedTextBorder);

            if (double.IsNaN(left))
            {
                left = 0;
            }

            if (double.IsNaN(top))
            {
                top = 0;
            }

            Canvas.SetLeft(_selectedTextBorder, Clamp(left + delta.X, 0, _surfaceWidth - GetBorderWidth(_selectedTextBorder)));
            Canvas.SetTop(_selectedTextBorder, Clamp(top + delta.Y, 0, _surfaceHeight - GetBorderHeight(_selectedTextBorder)));
            UpdateTextResizeThumbPositions(_selectedTextBorder);
        }

        private void SelectTextBorder(Border border)
        {
            _selectedTextBorder = border;
            UpdateTextResizeThumbPositions(border);
            SetTextResizeThumbsVisibility(Visibility.Visible);
        }

        private void ClearSelectedTextBorder()
        {
            _selectedTextBorder = null;
            SetTextResizeThumbsVisibility(Visibility.Collapsed);
        }

        private Rect GetTextBorderBounds(Border border)
        {
            double left = Canvas.GetLeft(border);
            double top = Canvas.GetTop(border);

            if (double.IsNaN(left))
            {
                left = 0;
            }

            if (double.IsNaN(top))
            {
                top = 0;
            }

            double width = Math.Max(MinTextWidth, GetBorderWidth(border));
            double height = Math.Max(MinTextHeight, GetBorderHeight(border));
            return new Rect(left, top, width, height);
        }

        private void UpdateTextResizeThumbPositions(Border border)
        {
            Rect bounds = GetTextBorderBounds(border);
            SetThumbPosition(TextTopLeftThumb, bounds.Left, bounds.Top);
            SetThumbPosition(TextTopRightThumb, bounds.Right, bounds.Top);
            SetThumbPosition(TextBottomRightThumb, bounds.Right, bounds.Bottom);
            SetThumbPosition(TextBottomLeftThumb, bounds.Left, bounds.Bottom);
        }

        private void SetTextResizeThumbsVisibility(Visibility visibility)
        {
            TextTopLeftThumb.Visibility = visibility;
            TextTopRightThumb.Visibility = visibility;
            TextBottomRightThumb.Visibility = visibility;
            TextBottomLeftThumb.Visibility = visibility;
        }

        private void TextResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (_selectedTextBorder is null || sender is not Thumb thumb || thumb.Tag is not string tag)
            {
                return;
            }

            Rect currentBounds = GetTextBorderBounds(_selectedTextBorder);
            double left = currentBounds.Left;
            double top = currentBounds.Top;
            double right = currentBounds.Right;
            double bottom = currentBounds.Bottom;

            if (tag.Contains("Left", StringComparison.Ordinal))
            {
                left = Clamp(left + e.HorizontalChange, 0, right - MinTextWidth);
            }

            if (tag.Contains("Right", StringComparison.Ordinal))
            {
                right = Clamp(right + e.HorizontalChange, left + MinTextWidth, _surfaceWidth);
            }

            if (tag.Contains("Top", StringComparison.Ordinal))
            {
                top = Clamp(top + e.VerticalChange, 0, bottom - MinTextHeight);
            }

            if (tag.Contains("Bottom", StringComparison.Ordinal))
            {
                bottom = Clamp(bottom + e.VerticalChange, top + MinTextHeight, _surfaceHeight);
            }

            _selectedTextBorder.Width = right - left;
            _selectedTextBorder.Height = bottom - top;
            Canvas.SetLeft(_selectedTextBorder, left);
            Canvas.SetTop(_selectedTextBorder, top);
            UpdateTextResizeThumbPositions(_selectedTextBorder);
            HintText.Text = "Text box resized.";
        }

        private void AutoSizeTextBorder(Border border)
        {
            if (border.Child is not TextBox textBox)
            {
                return;
            }

            double width = GetBorderWidth(border);
            textBox.Width = Math.Max(20, width - border.Padding.Left - border.Padding.Right);
            textBox.Measure(new Size(textBox.Width, double.PositiveInfinity));

            double desiredHeight = textBox.DesiredSize.Height + border.Padding.Top + border.Padding.Bottom;
            border.Height = Math.Max(MinTextHeight, desiredHeight);

            double left = Canvas.GetLeft(border);
            double top = Canvas.GetTop(border);
            if (double.IsNaN(left))
            {
                left = 0;
            }

            if (double.IsNaN(top))
            {
                top = 0;
            }

            Canvas.SetLeft(border, Clamp(left, 0, _surfaceWidth - GetBorderWidth(border)));
            Canvas.SetTop(border, Clamp(top, 0, _surfaceHeight - GetBorderHeight(border)));
            UpdateTextResizeThumbPositions(border);
        }

        private static double GetBorderWidth(Border border)
        {
            if (!double.IsNaN(border.Width) && border.Width > 0)
            {
                return border.Width;
            }

            if (border.ActualWidth > 0)
            {
                return border.ActualWidth;
            }

            return MinTextWidth;
        }

        private static double GetBorderHeight(Border border)
        {
            if (!double.IsNaN(border.Height) && border.Height > 0)
            {
                return border.Height;
            }

            if (border.ActualHeight > 0)
            {
                return border.ActualHeight;
            }

            return MinTextHeight;
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

            const double gap = 4;
            const double border = 8;

            double panelWidth = Math.Max(100, ToolPanel.ActualWidth);
            double panelHeight = Math.Max(40, ToolPanel.ActualHeight);

            bool isFullScreenSelection = selection.Left <= 1 && selection.Top <= 1 &&
                                         Math.Abs(selection.Width - _surfaceWidth) <= 2 &&
                                         Math.Abs(selection.Height - _surfaceHeight) <= 2;
            bool touchesTopEdge = selection.Top <= 1;
            bool touchesBottomEdge = Math.Abs(selection.Bottom - _surfaceHeight) <= 2;
            bool isFullHeightSelection = touchesTopEdge && touchesBottomEdge;

            double centeredX = Clamp(
                selection.Left + (selection.Width - panelWidth) / 2,
                border,
                _surfaceWidth - panelWidth - border);

            double topY = selection.Top - panelHeight - gap;
            double bottomY = selection.Bottom + gap;
            bool topFits = topY >= border;
            bool bottomFits = bottomY + panelHeight <= _surfaceHeight - border;

            double x = centeredX;
            double y;

            if (isFullScreenSelection || isFullHeightSelection)
            {
                x = Clamp(selection.Right - panelWidth - border, border, _surfaceWidth - panelWidth - border);
                y = Clamp(selection.Top + border, border, _surfaceHeight - panelHeight - border);
            }
            else if (!topFits && !bottomFits)
            {
                // For full-height/tall selections, pin toolbar inside selection to avoid off-screen placement.
                x = Clamp(selection.Right - panelWidth - border, border, _surfaceWidth - panelWidth - border);
                y = Clamp(selection.Top + border, border, _surfaceHeight - panelHeight - border);
            }
            else if (topFits)
            {
                y = topY;
            }
            else
            {
                y = Clamp(bottomY, border, _surfaceHeight - panelHeight - border);
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
            HintText.Text = "Selection updated.";
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

        private bool TryFindEditableShape(Point point, out Shape shape)
        {
            shape = null!;

            for (int i = OverlayCanvas.Children.Count - 1; i >= 0; i--)
            {
                if (OverlayCanvas.Children[i] is not Shape candidate || !_editableShapes.Contains(candidate))
                {
                    continue;
                }

                if (IsPointNearShapeEdge(candidate, point, 8))
                {
                    shape = candidate;
                    return true;
                }
            }

            return false;
        }

        private bool IsPointNearShapeEdge(Shape shape, Point point, double tolerance)
        {
            Rect bounds = GetShapeBounds(shape);
            if (bounds.IsEmpty)
            {
                return false;
            }

            if (shape is ShapePath path)
            {
                Geometry? data = path.Data;
                if (data is null)
                {
                    return false;
                }

                Pen pen = new(path.Stroke ?? Brushes.Transparent, Math.Max(2, shape.StrokeThickness + tolerance));
                return data.StrokeContains(pen, point);
            }

            if (shape is Rectangle)
            {
                Rect outer = new(bounds.Left - tolerance, bounds.Top - tolerance, bounds.Width + (2 * tolerance), bounds.Height + (2 * tolerance));
                Rect inner = new(
                    bounds.Left + tolerance,
                    bounds.Top + tolerance,
                    Math.Max(0, bounds.Width - (2 * tolerance)),
                    Math.Max(0, bounds.Height - (2 * tolerance)));
                return outer.Contains(point) && !inner.Contains(point);
            }

            if (shape is Ellipse)
            {
                double radiusX = Math.Max(0.1, bounds.Width / 2);
                double radiusY = Math.Max(0.1, bounds.Height / 2);
                double centerX = bounds.Left + radiusX;
                double centerY = bounds.Top + radiusY;
                double nx = (point.X - centerX) / radiusX;
                double ny = (point.Y - centerY) / radiusY;
                double normalized = (nx * nx) + (ny * ny);

                double outerX = radiusX + tolerance;
                double outerY = radiusY + tolerance;
                double innerX = Math.Max(0.1, radiusX - tolerance);
                double innerY = Math.Max(0.1, radiusY - tolerance);
                double nxOuter = (point.X - centerX) / outerX;
                double nyOuter = (point.Y - centerY) / outerY;
                double nxInner = (point.X - centerX) / innerX;
                double nyInner = (point.Y - centerY) / innerY;
                double outerValue = (nxOuter * nxOuter) + (nyOuter * nyOuter);
                double innerValue = (nxInner * nxInner) + (nyInner * nyInner);

                return outerValue <= 1.0 && innerValue >= 1.0 && normalized > 0;
            }

            return false;
        }

        private bool IsPointInsideShape(Shape shape, Point point)
        {
            if (shape is ShapePath path)
            {
                if (path.Data is null)
                {
                    return false;
                }

                Pen pen = new(path.Stroke ?? Brushes.Transparent, Math.Max(6, shape.StrokeThickness + 4));
                return path.Data.StrokeContains(pen, point);
            }

            return GetShapeBounds(shape).Contains(point);
        }

        private void SelectShape(Shape shape)
        {
            ClearSelectedTextBorder();
            _selectedShape = shape;
            SyncToolbarStyleFromShape(shape);
            UpdateSelectedShapeHandles();

            if (shape is ShapePath)
            {
                HintText.Text = "Arrow selected. Drag start/end points to resize.";
            }
            else
            {
                HintText.Text = "Shape selected. Drag corners to resize.";
            }
        }

        private void ClearSelectedShape()
        {
            _selectedShape = null;
            SetShapeResizeThumbsVisibility(Visibility.Collapsed);
            SetArrowEndpointThumbsVisibility(Visibility.Collapsed);
        }

        private void DeleteSelectedShape()
        {
            if (_selectedTextBorder is not null)
            {
                Border borderToDelete = _selectedTextBorder;
                double left = Canvas.GetLeft(borderToDelete);
                double top = Canvas.GetTop(borderToDelete);

                ClearSelectedTextBorder();
                _editableTextBorders.Remove(borderToDelete);
                OverlayCanvas.Children.Remove(borderToDelete);

                RegisterUndo(() =>
                {
                    if (!OverlayCanvas.Children.Contains(borderToDelete))
                    {
                        OverlayCanvas.Children.Add(borderToDelete);
                    }

                    _editableTextBorders.Add(borderToDelete);
                    Canvas.SetLeft(borderToDelete, left);
                    Canvas.SetTop(borderToDelete, top);
                    SelectTextBorder(borderToDelete);
                });

                HintText.Text = "Text deleted.";
                return;
            }

            if (_selectedShape is null)
            {
                return;
            }

            Shape target = _selectedShape;
            Rect targetBounds = GetShapeBounds(target);
            (Point Start, Point End)? deletedArrowAnchors = null;
            if (target is ShapePath arrow && _arrowAnchors.TryGetValue(arrow, out (Point Start, Point End) anchor))
            {
                deletedArrowAnchors = anchor;
            }

            ClearSelectedShape();
            _editableShapes.Remove(target);

            if (target is ShapePath removedArrow)
            {
                _arrowAnchors.Remove(removedArrow);
            }

            OverlayCanvas.Children.Remove(target);

            RegisterUndo(() =>
            {
                if (!OverlayCanvas.Children.Contains(target))
                {
                    OverlayCanvas.Children.Add(target);
                }

                _editableShapes.Add(target);
                if (target is ShapePath restoredArrow && deletedArrowAnchors is not null)
                {
                    _arrowAnchors[restoredArrow] = deletedArrowAnchors.Value;
                }

                if (target is Rectangle || target is Ellipse)
                {
                    Canvas.SetLeft(target, targetBounds.Left);
                    Canvas.SetTop(target, targetBounds.Top);
                }

                SelectShape(target);
            });

            HintText.Text = "Shape deleted.";
        }

        private Rect GetShapeBounds(Shape shape)
        {
            if (shape is ShapePath path)
            {
                return path.Data?.Bounds ?? Rect.Empty;
            }

            double left = Canvas.GetLeft(shape);
            double top = Canvas.GetTop(shape);

            if (double.IsNaN(left))
            {
                left = 0;
            }

            if (double.IsNaN(top))
            {
                top = 0;
            }

            return new Rect(left, top, Math.Max(MinShapeSize, shape.Width), Math.Max(MinShapeSize, shape.Height));
        }

        private void UpdateShapeResizeThumbPositions(Rect bounds)
        {
            if (bounds.IsEmpty)
            {
                SetShapeResizeThumbsVisibility(Visibility.Collapsed);
                return;
            }

            SetThumbPosition(ShapeTopLeftThumb, bounds.Left, bounds.Top);
            SetThumbPosition(ShapeTopRightThumb, bounds.Right, bounds.Top);
            SetThumbPosition(ShapeBottomRightThumb, bounds.Right, bounds.Bottom);
            SetThumbPosition(ShapeBottomLeftThumb, bounds.Left, bounds.Bottom);
        }

        private void UpdateEllipseResizeThumbPositions(Rect bounds)
        {
            if (bounds.IsEmpty)
            {
                SetShapeResizeThumbsVisibility(Visibility.Collapsed);
                return;
            }

            double centerX = bounds.Left + (bounds.Width / 2);
            double centerY = bounds.Top + (bounds.Height / 2);

            SetThumbPosition(ShapeTopLeftThumb, centerX, bounds.Top);
            SetThumbPosition(ShapeTopRightThumb, bounds.Right, centerY);
            SetThumbPosition(ShapeBottomRightThumb, centerX, bounds.Bottom);
            SetThumbPosition(ShapeBottomLeftThumb, bounds.Left, centerY);
        }

        private void UpdateSelectedShapeHandles()
        {
            if (_selectedShape is null)
            {
                SetShapeResizeThumbsVisibility(Visibility.Collapsed);
                SetArrowEndpointThumbsVisibility(Visibility.Collapsed);
                return;
            }

            if (_selectedShape is ShapePath arrow)
            {
                SetShapeResizeThumbsVisibility(Visibility.Collapsed);
                if (_arrowAnchors.TryGetValue(arrow, out (Point Start, Point End) anchors))
                {
                    UpdateArrowEndpointThumbPositions(anchors.Start, anchors.End);
                    SetArrowEndpointThumbsVisibility(Visibility.Visible);
                }
                else
                {
                    SetArrowEndpointThumbsVisibility(Visibility.Collapsed);
                }

                return;
            }

            SetArrowEndpointThumbsVisibility(Visibility.Collapsed);
            Rect bounds = GetShapeBounds(_selectedShape);
            if (_selectedShape is Ellipse)
            {
                ShapeTopLeftThumb.Cursor = Cursors.SizeNS;
                ShapeTopRightThumb.Cursor = Cursors.SizeWE;
                ShapeBottomRightThumb.Cursor = Cursors.SizeNS;
                ShapeBottomLeftThumb.Cursor = Cursors.SizeWE;
                UpdateEllipseResizeThumbPositions(bounds);
            }
            else
            {
                ShapeTopLeftThumb.Cursor = Cursors.SizeNWSE;
                ShapeTopRightThumb.Cursor = Cursors.SizeNESW;
                ShapeBottomRightThumb.Cursor = Cursors.SizeNWSE;
                ShapeBottomLeftThumb.Cursor = Cursors.SizeNESW;
                UpdateShapeResizeThumbPositions(bounds);
            }

            SetShapeResizeThumbsVisibility(Visibility.Visible);
        }

        private void SetShapeResizeThumbsVisibility(Visibility visibility)
        {
            ShapeTopLeftThumb.Visibility = visibility;
            ShapeTopRightThumb.Visibility = visibility;
            ShapeBottomRightThumb.Visibility = visibility;
            ShapeBottomLeftThumb.Visibility = visibility;
        }

        private void SetArrowEndpointThumbsVisibility(Visibility visibility)
        {
            ArrowStartThumb.Visibility = visibility;
            ArrowEndThumb.Visibility = visibility;
        }

        private void UpdateArrowEndpointThumbPositions(Point start, Point end)
        {
            SetThumbPosition(ArrowStartThumb, start.X, start.Y);
            SetThumbPosition(ArrowEndThumb, end.X, end.Y);
        }

        private void ShapeResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (_selectedShape is null || _selectedShape is ShapePath || sender is not Thumb thumb || thumb.Tag is not string tag)
            {
                return;
            }

            if (_selectedShape is Ellipse)
            {
                ResizeEllipseFromEdgeThumb(thumb, e);
                return;
            }

            Rect currentBounds = GetShapeBounds(_selectedShape);
            if (currentBounds.IsEmpty)
            {
                return;
            }

            double left = currentBounds.Left;
            double top = currentBounds.Top;
            double right = currentBounds.Right;
            double bottom = currentBounds.Bottom;

            if (tag.Contains("Left", StringComparison.Ordinal))
            {
                left = Clamp(left + e.HorizontalChange, 0, right - MinShapeSize);
            }

            if (tag.Contains("Right", StringComparison.Ordinal))
            {
                right = Clamp(right + e.HorizontalChange, left + MinShapeSize, _surfaceWidth);
            }

            if (tag.Contains("Top", StringComparison.Ordinal))
            {
                top = Clamp(top + e.VerticalChange, 0, bottom - MinShapeSize);
            }

            if (tag.Contains("Bottom", StringComparison.Ordinal))
            {
                bottom = Clamp(bottom + e.VerticalChange, top + MinShapeSize, _surfaceHeight);
            }

            Rect resizedBounds = new(left, top, right - left, bottom - top);
            ApplyShapeBounds(_selectedShape, currentBounds, resizedBounds);
            UpdateShapeResizeThumbPositions(resizedBounds);
        }

        private void ResizeEllipseFromEdgeThumb(Thumb thumb, DragDeltaEventArgs e)
        {
            if (_selectedShape is not Ellipse ellipse)
            {
                return;
            }

            Rect bounds = GetShapeBounds(ellipse);
            if (bounds.IsEmpty)
            {
                return;
            }

            double left = bounds.Left;
            double top = bounds.Top;
            double right = bounds.Right;
            double bottom = bounds.Bottom;

            if (ReferenceEquals(thumb, ShapeTopLeftThumb))
            {
                top = Clamp(top + e.VerticalChange, 0, bottom - MinShapeSize);
            }
            else if (ReferenceEquals(thumb, ShapeTopRightThumb))
            {
                right = Clamp(right + e.HorizontalChange, left + MinShapeSize, _surfaceWidth);
            }
            else if (ReferenceEquals(thumb, ShapeBottomRightThumb))
            {
                bottom = Clamp(bottom + e.VerticalChange, top + MinShapeSize, _surfaceHeight);
            }
            else if (ReferenceEquals(thumb, ShapeBottomLeftThumb))
            {
                left = Clamp(left + e.HorizontalChange, 0, right - MinShapeSize);
            }

            Rect resizedBounds = new(left, top, right - left, bottom - top);
            ApplyShapeBounds(ellipse, bounds, resizedBounds);
            UpdateEllipseResizeThumbPositions(resizedBounds);
        }

        private void ArrowEndpointThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (_selectedShape is not ShapePath arrow || sender is not Thumb thumb)
            {
                return;
            }

            if (!_arrowAnchors.TryGetValue(arrow, out (Point Start, Point End) anchors))
            {
                return;
            }

            Point start = anchors.Start;
            Point end = anchors.End;

            if (ReferenceEquals(thumb, ArrowStartThumb))
            {
                start = new Point(
                    Clamp(start.X + e.HorizontalChange, 0, _surfaceWidth),
                    Clamp(start.Y + e.VerticalChange, 0, _surfaceHeight));
            }
            else if (ReferenceEquals(thumb, ArrowEndThumb))
            {
                end = new Point(
                    Clamp(end.X + e.HorizontalChange, 0, _surfaceWidth),
                    Clamp(end.Y + e.VerticalChange, 0, _surfaceHeight));
            }
            else
            {
                return;
            }

            arrow.Data = BuildArrowGeometry(start, end);
            _arrowAnchors[arrow] = (start, end);
            UpdateArrowEndpointThumbPositions(start, end);
            HintText.Text = "Arrow resized.";
        }

        private void MoveSelectedShape(Vector delta)
        {
            if (_selectedShape is null)
            {
                return;
            }

            Rect currentBounds = GetShapeBounds(_selectedShape);
            if (currentBounds.IsEmpty)
            {
                return;
            }

            double moveX = delta.X;
            double moveY = delta.Y;

            if (currentBounds.Left + moveX < 0)
            {
                moveX = -currentBounds.Left;
            }

            if (currentBounds.Top + moveY < 0)
            {
                moveY = -currentBounds.Top;
            }

            if (currentBounds.Right + moveX > _surfaceWidth)
            {
                moveX = _surfaceWidth - currentBounds.Right;
            }

            if (currentBounds.Bottom + moveY > _surfaceHeight)
            {
                moveY = _surfaceHeight - currentBounds.Bottom;
            }

            if (_selectedShape is Rectangle || _selectedShape is Ellipse)
            {
                Canvas.SetLeft(_selectedShape, currentBounds.Left + moveX);
                Canvas.SetTop(_selectedShape, currentBounds.Top + moveY);
            }
            else if (_selectedShape is ShapePath arrow)
            {
                if (!_arrowAnchors.TryGetValue(arrow, out (Point Start, Point End) anchors) &&
                    _movingArrowInitialAnchors is not null)
                {
                    anchors = _movingArrowInitialAnchors.Value;
                }

                if (_arrowAnchors.TryGetValue(arrow, out anchors) || _movingArrowInitialAnchors is not null)
                {
                    if (!_arrowAnchors.TryGetValue(arrow, out anchors))
                    {
                        anchors = _movingArrowInitialAnchors!.Value;
                    }

                    Point start = new(anchors.Start.X + moveX, anchors.Start.Y + moveY);
                    Point end = new(anchors.End.X + moveX, anchors.End.Y + moveY);
                    arrow.Data = BuildArrowGeometry(start, end);
                    _arrowAnchors[arrow] = (start, end);
                }
            }

            UpdateSelectedShapeHandles();
        }

        private void ApplyShapeBounds(Shape shape, Rect oldBounds, Rect newBounds)
        {
            if (shape is Rectangle || shape is Ellipse)
            {
                Canvas.SetLeft(shape, newBounds.Left);
                Canvas.SetTop(shape, newBounds.Top);
                shape.Width = newBounds.Width;
                shape.Height = newBounds.Height;
                return;
            }

            if (shape is ShapePath arrow && _arrowAnchors.TryGetValue(arrow, out (Point Start, Point End) anchor))
            {
                Point start = MapPointBetweenRects(anchor.Start, oldBounds, newBounds);
                Point end = MapPointBetweenRects(anchor.End, oldBounds, newBounds);
                arrow.Data = BuildArrowGeometry(start, end);
                _arrowAnchors[arrow] = (start, end);
            }
        }

        private static Point MapPointBetweenRects(Point point, Rect oldBounds, Rect newBounds)
        {
            double normalizedX = oldBounds.Width <= 0.001
                ? 0.5
                : (point.X - oldBounds.Left) / oldBounds.Width;
            double normalizedY = oldBounds.Height <= 0.001
                ? 0.5
                : (point.Y - oldBounds.Top) / oldBounds.Height;

            return new Point(
                newBounds.Left + (normalizedX * newBounds.Width),
                newBounds.Top + (normalizedY * newBounds.Height));
        }

        private void RegisterUndo(Action undoAction)
        {
            _undoActions.Push(undoAction);
        }

        private bool MatchesShortcut(KeyEventArgs e, string actionKey)
        {
            if (!_shortcutGestures.TryGetValue(actionKey, out (Key Key, ModifierKeys Modifiers) configured))
                return false;

            Key pressed = e.Key == Key.System ? e.SystemKey : e.Key;
            return pressed == configured.Key && Keyboard.Modifiers == configured.Modifiers;
        }

        private bool TryHandleShortcut(KeyEventArgs e)
        {
            if (MatchesShortcut(e, "undo"))
            {
                UndoLastAction();
                return true;
            }

            if (MatchesShortcut(e, "save"))
            {
                SaveButton_Click(this, new RoutedEventArgs());
                return true;
            }

            if (MatchesShortcut(e, "copy"))
            {
                CopyButton_Click(this, new RoutedEventArgs());
                return true;
            }

            if (MatchesShortcut(e, "delete"))
            {
                DeleteSelectedShape();
                return true;
            }

            if (MatchesShortcut(e, "cancel"))
            {
                CancelButton_Click(this, new RoutedEventArgs());
                return true;
            }

            if (MatchesShortcut(e, "toolRect"))
            {
                ApplyToolSelection(EditorTool.Rectangle);
                return true;
            }

            if (MatchesShortcut(e, "toolEllipse"))
            {
                ApplyToolSelection(EditorTool.Ellipse);
                return true;
            }

            if (MatchesShortcut(e, "toolArrow"))
            {
                ApplyToolSelection(EditorTool.Arrow);
                return true;
            }

            if (MatchesShortcut(e, "toolText"))
            {
                ApplyToolSelection(EditorTool.Text);
                return true;
            }

            if (MatchesShortcut(e, "toolNumber"))
            {
                ApplyToolSelection(EditorTool.Number);
                return true;
            }

            return false;
        }

        private void ColorButton_Click(object sender, RoutedEventArgs e)
        {
            if (FindResource("ColorPickerMenu") is not ContextMenu colorMenu)
            {
                return;
            }

            colorMenu.PlacementTarget = ColorButton;
            colorMenu.IsOpen = true;
        }

        private void StrokeButton_Click(object sender, RoutedEventArgs e)
        {
            if (FindResource("StrokePickerMenu") is not ContextMenu strokeMenu)
            {
                return;
            }

            strokeMenu.PlacementTarget = StrokeButton;
            strokeMenu.IsOpen = true;
        }

        private void ColorMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || menuItem.Tag is not string rawColor)
            {
                return;
            }

            if (ColorConverter.ConvertFromString(rawColor) is not Color selectedColor)
            {
                return;
            }

            _currentAnnotationColor = selectedColor;
            UpdateCurrentColorSwatch();
            ApplyCurrentStyleToSelection();
        }

        private void StrokeMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem menuItem || menuItem.Tag is not string rawThickness)
            {
                return;
            }

            if (!double.TryParse(rawThickness, NumberStyles.Float, CultureInfo.InvariantCulture, out double selectedThickness))
            {
                return;
            }

            _currentStrokeThickness = selectedThickness;
            UpdateCurrentStrokePreview();
            ApplyCurrentStyleToSelection();
        }

        private void ApplyCurrentStyleToSelection()
        {
            if (_selectedShape is not null)
            {
                _selectedShape.Stroke = new SolidColorBrush(_currentAnnotationColor);
                _selectedShape.StrokeThickness = _currentStrokeThickness;
                UpdateSelectedShapeHandles();
            }

            if (_selectedTextBorder?.Child is TextBox textBox)
            {
                textBox.Foreground = new SolidColorBrush(_currentAnnotationColor);
            }
        }

        private void UpdateCurrentColorSwatch()
        {
            CurrentColorSwatch.Fill = new SolidColorBrush(_currentAnnotationColor);
        }

        private void UpdateCurrentStrokePreview()
        {
            CurrentStrokePreview.Height = _currentStrokeThickness;
        }

        private void SyncToolbarStyleFromShape(Shape shape)
        {
            if (shape.Stroke is SolidColorBrush strokeBrush)
            {
                _currentAnnotationColor = strokeBrush.Color;
                UpdateCurrentColorSwatch();
            }

            _currentStrokeThickness = Math.Max(2, shape.StrokeThickness);
            UpdateCurrentStrokePreview();
        }

        private static Color DarkenColor(Color color, double factor)
        {
            return Color.FromRgb(
                (byte)Math.Clamp((int)(color.R * factor), 0, 255),
                (byte)Math.Clamp((int)(color.G * factor), 0, 255),
                (byte)Math.Clamp((int)(color.B * factor), 0, 255));
        }

        private void UndoLastAction()
        {
            if (_undoActions.Count == 0)
            {
                HintText.Text = "Nothing to undo.";
                return;
            }

            Action undoAction = _undoActions.Pop();
            undoAction();
            HintText.Text = "Undone.";
        }

        private void UndoButton_Click(object sender, RoutedEventArgs e)
        {
            UndoLastAction();
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BitmapSource image = RenderSelectionBitmap(GetExportSelection());
                Clipboard.SetImage(image);
                SavedFilePath = null;
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                HintText.Text = "Copy failed: " + ex.Message;
            }
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                BitmapSource image = RenderSelectionBitmap(GetExportSelection());
                SaveSelectionToFile(image);
                Clipboard.SetImage(image);
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                HintText.Text = "Save failed: " + ex.Message;
            }
        }

        private void SaveSelectionToFile(BitmapSource image)
        {
            string fileName = DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_edited.png";
            string filePath = System.IO.Path.Combine(_outputFolder, fileName);

            PngBitmapEncoder encoder = new();
            encoder.Frames.Add(BitmapFrame.Create(image));

            using FileStream stream = new(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
            encoder.Save(stream);

            SavedFilePath = filePath;
        }

        private Rect GetExportSelection()
        {
            return _hasSelection
                ? _selectionRect
                : new Rect(0, 0, OverlayCanvas.Width, OverlayCanvas.Height);
        }

        private BitmapSource RenderSelectionBitmap(Rect selection)
        {
            Visibility selectionVisibility = SelectionRectangle.Visibility;
            Visibility previewVisibility = SelectedPreview.Visibility;
            Visibility toolPanelVisibility = ToolPanel.Visibility;
            Visibility resizeThumbVisibility = TopLeftThumb.Visibility;
            Visibility shapeResizeThumbVisibility = ShapeTopLeftThumb.Visibility;
            Visibility arrowEndpointThumbVisibility = ArrowStartThumb.Visibility;
            Visibility textResizeThumbVisibility = TextTopLeftThumb.Visibility;
            Visibility dimOverlayVisibility = DimOverlay.Visibility;
            Geometry? previewClip = SelectedPreview.Clip;

            SelectionRectangle.Visibility = Visibility.Collapsed;
            ToolPanel.Visibility = Visibility.Collapsed;
            SetResizeThumbsVisibility(Visibility.Collapsed);
            SetShapeResizeThumbsVisibility(Visibility.Collapsed);
            SetArrowEndpointThumbsVisibility(Visibility.Collapsed);
            SetTextResizeThumbsVisibility(Visibility.Collapsed);

            if (IsFullSurfaceSelection(selection))
            {
                DimOverlay.Visibility = Visibility.Collapsed;
                SelectedPreview.Visibility = Visibility.Collapsed;
                SelectedPreview.Clip = null;
            }

            try
            {
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
                cropped.Freeze();
                return cropped;
            }
            finally
            {
                SelectionRectangle.Visibility = selectionVisibility;
                SelectedPreview.Visibility = previewVisibility;
                SelectedPreview.Clip = previewClip;
                ToolPanel.Visibility = toolPanelVisibility;
                SetResizeThumbsVisibility(resizeThumbVisibility);
                SetShapeResizeThumbsVisibility(shapeResizeThumbVisibility);
                SetArrowEndpointThumbsVisibility(arrowEndpointThumbVisibility);
                SetTextResizeThumbsVisibility(textResizeThumbVisibility);
                DimOverlay.Visibility = dimOverlayVisibility;
            }
        }

        private bool IsFullSurfaceSelection(Rect selection)
        {
            Rect normalized = NormalizeRect(selection.TopLeft, selection.BottomRight);
            return normalized.Left <= 1 && normalized.Top <= 1 &&
                   Math.Abs(normalized.Width - OverlayCanvas.Width) <= 2 &&
                   Math.Abs(normalized.Height - OverlayCanvas.Height) <= 2;
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (!_shortcutsEnabled)
            {
                return;
            }

            if (Keyboard.FocusedElement is TextBox)
            {
                return;
            }

            if (TryHandleShortcut(e))
            {
                e.Handled = true;
                return;
            }
        }
    }
}

