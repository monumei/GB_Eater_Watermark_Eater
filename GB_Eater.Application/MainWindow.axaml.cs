using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace GB_Eater.Application;

public partial class MainWindow : Window
{
    private SKBitmap? _originalImage;
    private SKBitmap? _protectedImage;
    private SKBitmap? _watermarkBitmap;
    
    // Dragging state
    private bool _isDraggingWatermark = false;
    private bool _isPanningView = false;
    private Avalonia.Point _lastPointerPos;
    private Avalonia.Media.TranslateTransform _watermarkTranslateTransform = new Avalonia.Media.TranslateTransform();
    private Avalonia.Media.ScaleTransform _watermarkScaleTransform = new Avalonia.Media.ScaleTransform();
    private Avalonia.Media.ScaleTransform? _viewScaleTransform;
    private Avalonia.Media.TranslateTransform? _viewTranslateTransform;

    // Enum for convenience


    public MainWindow()
    {
        InitializeComponent();
        
        // Initialize Watermark Transforms from XAML Group
        if (WatermarkOverlay.RenderTransform is Avalonia.Media.TransformGroup wGroup)
        {
             _watermarkScaleTransform = wGroup.Children[0] as Avalonia.Media.ScaleTransform ?? new Avalonia.Media.ScaleTransform();
             _watermarkTranslateTransform = wGroup.Children[1] as Avalonia.Media.TranslateTransform ?? new Avalonia.Media.TranslateTransform();
        }

        // Retrieve View Transforms manually since code-gen might fail for nested named items in TransformGroup
        // Structure in XAML: Panel_RenderTransform -> TransformGroup -> [Scale, Translate]
        if (PreviewContainer.RenderTransform is Avalonia.Media.TransformGroup group)
        {
             _viewScaleTransform = group.Children[0] as Avalonia.Media.ScaleTransform;
             _viewTranslateTransform = group.Children[1] as Avalonia.Media.TranslateTransform;
        }

        // Event for Slider
        SliderWatermarkSize.ValueChanged += (s, e) => {
            _watermarkScaleTransform.ScaleX = e.NewValue;
            _watermarkScaleTransform.ScaleY = e.NewValue;
        };
    }

    private async void OnUploadWatermarkClick(object? sender, RoutedEventArgs e)
    {
         var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Watermark Image",
            AllowMultiple = false,
            FileTypeFilter = new[] { FilePickerFileTypes.ImageAll }
        });

        if (files.Count > 0)
        {
            var file = files[0];
            using var stream = await file.OpenReadAsync();
            
            _watermarkBitmap?.Dispose();
            _watermarkBitmap = SKBitmap.Decode(stream);
            
            // Display in Overlay
            using var imgStream = new MemoryStream();
            _watermarkBitmap.Encode(imgStream, SKEncodedImageFormat.Png, 100);
            imgStream.Seek(0, SeekOrigin.Begin);
            
            WatermarkOverlay.Source = new Bitmap(imgStream);
            WatermarkOverlay.IsVisible = true;
            
            // Center it initially (roughly)
            _watermarkTranslateTransform.X = 50;
            _watermarkTranslateTransform.Y = 50;
            
            // Update opacity slider
            WatermarkOverlay.Opacity = SliderOpacity.Value / 100.0;
        }
    }

    private void OnPreviewPointerWheelChanged(object? sender, Avalonia.Input.PointerWheelEventArgs e)
    {

        
        // Simple Zoom
        if (_viewScaleTransform == null || _viewTranslateTransform == null) return;

        var zoomFactor = e.Delta.Y > 0 ? 1.1 : 0.9;
        
        var currentScale = _viewScaleTransform.ScaleX;
        var newScale = currentScale * zoomFactor;
        
        // Limit zoom
        if (newScale < 0.1) newScale = 0.1;
        if (newScale > 10) newScale = 10;
        
        // Zoom towards pointer
        // 1. Get pointer relative to the container (Panel)
        // If we just scale the container, the point under cursor changes.
        // We want point under cursor to stay static.
        
        // Actually, easiest is to just Scale around Center? 
        // Or specific logic.
        
        // To zoom around pointer:
        // P_new = P_old * scale_change + T_new - T_old... tricky with transform group.
        
        // Let's rely on RenderTransformOrigin behavior or manual calc.
        // The container is the target.
        // But PreviewContainer RenderTransformOrigin is 0.5,0.5 by default?
        // Let's just adjust Scale and Translate manually.
        
        // Position relative to Border (viewport)
        var pointerPos = e.GetPosition(PreviewBorder);
        
        // Relative to Content (before new zoom)
        var relativePos = e.GetPosition(PreviewContainer); // This is in Local Space
        
        _viewScaleTransform.ScaleX = newScale;
        _viewScaleTransform.ScaleY = newScale;
        
        // Adjust translation to keep relativePos at pointerPos
        // We need the Point in Parent coords.
        // P_screen = P_local * Scale + Translate
        // We want P_screen to stay same for P_local.
        // Translate = P_screen - (P_local * Scale)
        
        // P_local is e.GetPosition(PreviewContainer) *relative to bounds 0,0*
        // Wait, GetPosition(PreviewContainer) already accounts for current transform invert?
        // Yes, if we are inside.
        
        _viewTranslateTransform.X = pointerPos.X - (relativePos.X * newScale);
        _viewTranslateTransform.Y = pointerPos.Y - (relativePos.Y * newScale);
        
        ClampView();

        e.Handled = true;
    }

    private void ClampView()
    {
        if (_viewTranslateTransform == null || _viewScaleTransform == null) return;

        var bounds = PreviewContainer.Bounds;
        var scale = _viewScaleTransform.ScaleX;

        double zoomedW = bounds.Width * scale;
        double zoomedH = bounds.Height * scale;

        double minX, maxX;
        if (zoomedW > bounds.Width)
        {
            maxX = 0;
            minX = bounds.Width - zoomedW;
        }
        else
        {
            minX = 0;
            maxX = bounds.Width - zoomedW;
        }

        double minY, maxY;
        if (zoomedH > bounds.Height)
        {
            maxY = 0;
            minY = bounds.Height - zoomedH;
        }
        else
        {
            minY = 0;
            maxY = bounds.Height - zoomedH;
        }

        _viewTranslateTransform.X = Math.Max(minX, Math.Min(maxX, _viewTranslateTransform.X));
        _viewTranslateTransform.Y = Math.Max(minY, Math.Min(maxY, _viewTranslateTransform.Y));
    }

    // Pointer Events for Dragging & Panning
    private void OnPreviewPointerPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        var props = e.GetCurrentPoint(PreviewContainer).Properties;
        
        // Right Click or Middle -> Pan
        if (props.IsRightButtonPressed || props.IsMiddleButtonPressed)
        {
            _isPanningView = true;
            _lastPointerPos = e.GetPosition(PreviewBorder); // Use Parent coords for panning delta
            e.Pointer.Capture(PreviewContainer);
            return;
        }

        // Left Click -> Check Watermark
        if (props.IsLeftButtonPressed && WatermarkOverlay.IsVisible && _watermarkBitmap != null)
        {
            var pos = e.GetPosition(PreviewContainer);
            
            // Assuming HorizontalAlignment="Left" VerticalAlignment="Top" for WatermarkOverlay
            double wx = _watermarkTranslateTransform.X;
            double wy = _watermarkTranslateTransform.Y;
            double scale = _watermarkScaleTransform.ScaleX;
            
            // Approximate hit test with scale
            // The Bounds check gives unscaled bounds usually if Transform is applied at Render level?
            // Actually Bounds might be unscaled.
            // Let's use Bitmap size * Scale
            
            double w = _watermarkBitmap.Width * scale; // Note: Bitmap pixels vs Display pixels might differ if DPI??
            // Wait, Avalonia Image size is determined by Source size unless stretched? 
            // Stretch="None" means it matches source pixel size (logical pixels).
            // Let's assume logical size ~ bitmap size for simplicity or use WatermarkOverlay.Bounds.Width * scale
            // But WatermarkOverlay.Bounds changes if layout runs? RenderTransform doesn't affect Layout Bounds usually.
            
            double baseW = WatermarkOverlay.Bounds.Width;
            double baseH = WatermarkOverlay.Bounds.Height;
            
            if (pos.X >= wx && pos.X <= wx + (baseW * scale) &&
                pos.Y >= wy && pos.Y <= wy + (baseH * scale))
            {
                 _isDraggingWatermark = true;
                 _lastPointerPos = pos;
                 e.Pointer.Capture(PreviewContainer);
            }
        }
    }

    private void OnPreviewPointerMoved(object? sender, Avalonia.Input.PointerEventArgs e)
    {
        if (_isPanningView)
        {
            var pos = e.GetPosition(PreviewBorder);
            var delta = pos - _lastPointerPos;
            
            // Calculate proposed
            _viewTranslateTransform!.X += delta.X;
            _viewTranslateTransform!.Y += delta.Y;
            
            ClampView();
            
            _lastPointerPos = pos;
            return;
        }
    
        if (_isDraggingWatermark)
        {
            // Dragging Watermark relies on LOCAL coordinates inside the Container
            var pos = e.GetPosition(PreviewContainer);
            var delta = pos - _lastPointerPos;
            
            // Since we use GetPosition(Container), and Container is the matched coordinate space for overlay,
            // this delta works regardless of Zoom level!
            
            _watermarkTranslateTransform.X += delta.X;
            _watermarkTranslateTransform.Y += delta.Y;
            
            _lastPointerPos = pos;
        }
    }

    private void OnPreviewPointerReleased(object? sender, Avalonia.Input.PointerReleasedEventArgs e)
    {
        if (_isDraggingWatermark)
        {
            _isDraggingWatermark = false;
            e.Pointer.Capture(null);
        }
        if (_isPanningView)
        {
            _isPanningView = false;
            e.Pointer.Capture(null);
        }
    }

    private async void OnLoadClick(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open Image",
            AllowMultiple = false,
            FileTypeFilter = new[] { FilePickerFileTypes.ImageAll }
        });

        if (files.Count > 0)
        {
            var file = files[0];
            using var stream = await file.OpenReadAsync();
            
            // Dispose previous
            _originalImage?.Dispose();
            
            _originalImage = SKBitmap.Decode(stream);
            
            // Ensure format is compatible (BGRA recommended for manipulation)
            if (_originalImage.ColorType != SKColorType.Bgra8888)
            {
                var converted = _originalImage.Copy(SKColorType.Bgra8888);
                _originalImage.Dispose();
                _originalImage = converted;
            }

            // Show original
            DisplayImage(_originalImage);
            BtnSave.IsEnabled = true;
            
            // Reset state
            _protectedImage?.Dispose();
            _protectedImage = null;
        }
    }

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        if (_protectedImage == null && _originalImage == null) return;
        var methodImage = _protectedImage ?? _originalImage;

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Save Protected Image",
            DefaultExtension = "png",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("PNG Image") { Patterns = new[] { "*.png" } },
                new FilePickerFileType("JPEG Image") { Patterns = new[] { "*.jpg", "*.jpeg" } }
            }
        });

        if (file != null)
        {
            using var stream = await file.OpenWriteAsync();
            var path = file.Path.ToString(); // Helper to check extension
            
            SKEncodedImageFormat fmt = SKEncodedImageFormat.Png;
            if (path.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || 
                path.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                fmt = SKEncodedImageFormat.Jpeg;
            }

            methodImage!.Encode(stream, fmt, 100);
        }
    }

    private void OnProtectClick(object? sender, RoutedEventArgs e)
    {
        if (_originalImage == null) return;

        // Params
        int strength = (int)SliderStrength.Value;
        bool addWatermark = ChkWatermark.IsChecked ?? false;
        // string watermarkText = TxtWatermark.Text ?? ""; // Removed per user request
        float opacity = (float)SliderOpacity.Value / 100f;
        float scale = (float)SliderWatermarkSize.Value;
        ProtectMode mode = (ProtectMode)CmbMode.SelectedIndex;

        // 1. Noise Protection
        // Create a copy for processing
        using var tempBase = ImageProcessor.ProtectImage(_originalImage, strength, mode);

        // 2. Watermark
        if (addWatermark)
        {
            if (_watermarkBitmap != null)
            {
                // Image Watermark:
                // We burn it into _protectedImage for SAVING.
                // But for DISPLAY, we show the Noise-only image + The Interactive Overlay.
                // But for DISPLAY, we show the Noise-only image + The Interactive Overlay.
                _protectedImage?.Dispose();
                _protectedImage = ApplyImageWatermark(tempBase, _watermarkBitmap, opacity, scale);
                
                // Display the noise-only base, so overlay sits on top without duplication
                DisplayImage(tempBase);
            }
            else
            {
               // No text watermark support anymore. 
               // Just treat as separate layer or nothing?
               // If user checked "Add Watermark" but didn't upload image, effectively nothing happens or just noise protection.
               _protectedImage?.Dispose();
               _protectedImage = tempBase.Copy();
               DisplayImage(_protectedImage);
            }
        }
        else
        {
            _protectedImage?.Dispose();
            _protectedImage = tempBase.Copy();
            DisplayImage(_protectedImage);
        }

        // Also update overlay opacity just in case
        if (WatermarkOverlay.IsVisible)
        {
            WatermarkOverlay.Opacity = opacity;
        }
    }

    SKBitmap ApplyImageWatermark(SKBitmap baseImg, SKBitmap watermark, float opacity, float scaleVis)
    {
        SKBitmap result = baseImg.Copy();
        using (SKCanvas canvas = new SKCanvas(result))
        {
            // Map visual coordinates to image coordinates
            // 1. Get Visual Bounds of the displayed image
            var panelSize = PreviewContainer.Bounds.Size;
            var imgSize = new Avalonia.Size(baseImg.Width, baseImg.Height);
            
            // Uniform Stretch logic
            double scaleX = panelSize.Width / imgSize.Width;
            double scaleY = panelSize.Height / imgSize.Height;
            double scale = Math.Min(scaleX, scaleY);
            
            double displayedW = imgSize.Width * scale;
            double displayedH = imgSize.Height * scale;
            
            double offsetX = (panelSize.Width - displayedW) / 2;
            double offsetY = (panelSize.Height - displayedH) / 2;
            
            // Watermark visual pos
            double wx = _watermarkTranslateTransform.X;
            double wy = _watermarkTranslateTransform.Y;
            
            // Relative to image visual
            double relX = wx - offsetX;
            double relY = wy - offsetY;
            
            // Map to actual image coords
            double finalX = relX / scale;
            double finalY = relY / scale;
            
            // Draw
            using var paint = new SKPaint
            {
                Color = SKColors.White.WithAlpha((byte)(opacity * 255)),
                IsAntialias = true,
                FilterQuality = SKFilterQuality.High
            };
            
            // Note: If opacity is applied via Paint.Color Alpha, it tints the image?
            // No, DrawBitmap with Paint applies alpha modulation if Color is white.
            
            // Scale the watermark bitmap
            // Target size in image pixels
            // displayedW is how wide the base image looks constantly on screen.
            // Screen watermark size = watermark.Width * scaleVis
            // We need to map (watermark.Width * scaleVis) back to image coords.
            
            // screen_size = image_size * global_scale
            // image_size = screen_size / global_scale
            
            double targetW = (watermark.Width * scaleVis) / scale;
            double targetH = (watermark.Height * scaleVis) / scale;
            
            var destRect = SKRect.Create((float)finalX, (float)finalY, (float)targetW, (float)targetH);
            
            canvas.DrawBitmap(watermark, destRect, paint);
        }
        return result;
    }

    private void DisplayImage(SKBitmap? bmp)
    {
        if (bmp == null)
        {
            PreviewImage.Source = null;
            return;
        }

        // Convert SKBitmap to Avalonia Bitmap
        using var image = SKImage.FromBitmap(bmp);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream();
        data.SaveTo(stream);
        stream.Seek(0, SeekOrigin.Begin);
        
        PreviewImage.Source = new Bitmap(stream);
    }

}
