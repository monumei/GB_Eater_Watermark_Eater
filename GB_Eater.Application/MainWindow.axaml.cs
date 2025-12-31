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
    enum ProtectMode { Soft = 0, Balanced = 1, Strong = 2, AIPoison = 3 }

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
        
        e.Handled = true;
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
            
            _viewTranslateTransform!.X += delta.X;
            _viewTranslateTransform!.Y += delta.Y;
            
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
        using var tempBase = ProtectImage(_originalImage, strength, mode);

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

    // ================= LOGIC PORT =================

    int Clamp(int v) => Math.Max(0, Math.Min(255, v));

    SKBitmap ProtectImage(SKBitmap src, int strength, ProtectMode mode)
    {
        SKBitmap result = src.Copy();
        Random rng = new Random(Guid.NewGuid().GetHashCode());

        switch (mode)
        {
            case ProtectMode.Soft:
                ApplyBalancedNoise(result, strength / 2, rng);
                ColorShift(result, strength / 4, rng);
                break;
            case ProtectMode.Balanced:
                ApplyBalancedNoise(result, strength, rng);
                EdgeJitter(result, strength / 2);
                AdversarialNoise(result, strength / 3, rng);
                break;
            case ProtectMode.Strong:
                ApplyBalancedNoise(result, strength, rng);
                EdgeJitter(result, strength);
                TextureNoise(result, strength / 2, rng);
                ColorShift(result, strength / 2, rng);
                GeometricDistortion(result, strength / 5, rng);
                break;
            case ProtectMode.AIPoison:
                // 1. Sine Interference (Global frequency disruption)
                SineInterference(result, (int)(strength * 0.5));
            
                // 2. Geometric distortion (Boosted)
                GeometricDistortion(result, (int)(strength * 0.5), rng);
                
                // 3. Adversarial Noise (Grid High-Freq)
                AdversarialNoise(result, (int)(strength * 0.8), rng);
                
                // 4. Color shifting (Boosted)
                ColorShift(result, (int)(strength * 0.8), rng);
                
                // 5. Block Local Scramble
                BlockLocalScramble(result, (int)(strength * 0.5), rng);
                break;
        }
        return result;
    }

    unsafe void ApplyBalancedNoise(SKBitmap bmp, int strength, Random rng)
    {
        int w = bmp.Width;
        int h = bmp.Height;
        
        // Ensure format is Bgra8888 for easier pointer math
        // (We ensure this on load, but good to be safe if reused)
        
        byte* ptr = (byte*)bmp.GetPixels();
        
        // SKColorType.Bgra8888: B, G, R, A
        
        int totalBytes = w * h * 4;
        
        for (int i = 0; i < totalBytes; i += 4)
        {
            byte b = ptr[i];
            byte g = ptr[i+1];
            byte r = ptr[i+2];
            byte a = ptr[i+3];

            if (a == 0) continue;

            int lum = (int)(0.299 * r + 0.587 * g + 0.114 * b);
            int noise = rng.Next(-strength, strength + 1);
            int newLum = Clamp(lum + noise);

            float ratio = lum == 0 ? 1f : (float)newLum / lum;

            ptr[i]   = (byte)Clamp((int)(b * ratio)); // B
            ptr[i+1] = (byte)Clamp((int)(g * ratio)); // G
            ptr[i+2] = (byte)Clamp((int)(r * ratio)); // R
            // Alpha unchanged
        }
    }

    unsafe void TextureNoise(SKBitmap bmp, int strength, Random rng)
    {
        int w = bmp.Width;
        int h = bmp.Height;
        uint* ptr = (uint*)bmp.GetPixels();

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                if ((x + y) % 3 != 0) continue;

                int offset = y * w + x;
                uint pixel = ptr[offset];
                
                byte a = (byte)((pixel >> 24) & 0xFF);
                if (a == 0) continue;
                
                byte r = (byte)((pixel >> 16) & 0xFF);
                byte g = (byte)((pixel >> 8) & 0xFF);
                byte b = (byte)(pixel & 0xFF);

                int n = rng.Next(-strength, strength + 1);

                r = (byte)Clamp(r + n);
                g = (byte)Clamp(g + n);
                b = (byte)Clamp(b + n);

                ptr[offset] = (uint)((a << 24) | (r << 16) | (g << 8) | b);
            }
        }
    }

    unsafe void EdgeJitter(SKBitmap bmp, int strength)
    {
        // Edge jitter moves pixels. Operating in place is tricky, creates trails.
        // Original code: "Bitmap bmp = new Bitmap(src);" but here `bmp` is passed as result.
        // Actually original Logic: `TextureNoise` made a copy, `EdgeJitter` made a copy.
        // My `ProtectImage` makes a copy initially. `ApplyBalancedNoise` modifies in place.
        // `EdgeJitter` needs to read from original state or current state? 
        // Original: `bmp.SetPixel(x+1, y, c)` -> writes to *new* (or same) bitmap.
        // The original code `EdgeJitter` created `new Bitmap(src)` and modified it. 
        // Wait, if it modifies `x, y` based on `x, y` of src, it's fine.
        // But the original code loop:
        // for y... for x... SetPixel(x + 1, y, c).
        // It reads from `bmp` (the copy) and writes to `bmp` (the copy).
        // Wait, `Bitmap bmp = new Bitmap(src);`
        // In original `ProtectImage`: `result = EdgeJitter(result, ...)`
        // `EdgeJitter` function: `Bitmap bmp = new Bitmap(src); ... return bmp;`
        // So it creates a NEW copy from the input.
        
        SKBitmap copy = bmp.Copy();
        uint* srcPtr = (uint*)bmp.GetPixels(); // Read from current
        uint* destPtr = (uint*)copy.GetPixels(); // Write to new

        int w = bmp.Width;
        int h = bmp.Height;

        for (int y = 1; y < h - 1; y++)
        {
            for (int x = 1; x < w - 1; x++)
            {
                if ((x + y) % 4 != 0) continue;

                // Move pixel at (x,y) to (x+1, y)
                // Original: `Color c = bmp.GetPixel(x, y); bmp.SetPixel(x+1, y, c);` 
                // (Note: `src` in original was the input, `bmp` was the copy initialized with `src`. 
                // Actually original code: `Bitmap bmp = new Bitmap(src);`... `Color c = bmp.GetPixel`... `bmp.SetPixel`
                // So it was reading and writing to the SAME bitmap potentially overwriting what it reads later?
                // `SetPixel(x+1)` might affect next iteration of `x`? 
                // No, standard loop x++. `x` becomes `x+1` next.
                // It copies (x,y) to (x+1, y). The next iteration reads (x+1, y) which is now the OLD (x,y).
                // This creates a smear. We will replicate that behavior because maybe it's desired.
                // But generally "Jitter" implies randomized or swapping. 
                // I will replicate the "read from copy, write to copy" behavior.

                int srcOffset = y * w + x;
                int destOffset = y * w + (x + 1);
                
                destPtr[destOffset] = srcPtr[srcOffset]; // Read from original state, write to new state
            }
        }
        
        // Update the reference in ProtectImage? 
        // No, `ProtectImage` logic needs to handle the swap.
        // Accessing helper function like this in C# converts pointers? No.
        
        // To properly implement:
        // `EdgeJitter` should return a new Bitmap or we copy pixels back.
        // Since I'm inside `ProtectImage`, I should swap.
        
        // But wait, `EdgeJitter` is void here? 
        // I need to change signature or implementation.
        // I will change implementation to copy pixels back to `bmp`.
        
        // Actually, just copying `copy` content back to `bmp` at the end is fastest.
        // `bmp` is the `result` in ProtectImage.
        
        // MemCpy
        Buffer.MemoryCopy(destPtr, srcPtr, w * h * 4, w * h * 4);
        copy.Dispose();
    }

    SKBitmap ApplyTiledCircularWatermark(SKBitmap src, string text, float opacity, int tileSize, int repeatPerCircle)
    {
        SKBitmap bmp = new SKBitmap(src.Width, src.Height);
        using (SKCanvas canvas = new SKCanvas(bmp))
        {
            canvas.DrawBitmap(src, 0, 0);

            using var paint = new SKPaint
            {
                Color = SKColors.White.WithAlpha((byte)(opacity * 255)),
                IsAntialias = true,
                TextSize = Math.Max(12, src.Width / 40f),
                Typeface = SKTypeface.FromFamilyName("Arial", SKFontStyle.Bold),
                TextAlign = SKTextAlign.Center
            };
            
            // Adjust baseline centering if needed
            SKRect textBounds = new SKRect();
            paint.MeasureText(text, ref textBounds);
            float textHeight = textBounds.Height;

            float radius = tileSize * 0.35f;

            for (int ty = 0; ty < src.Height + tileSize; ty += tileSize)
            {
                for (int tx = 0; tx < src.Width + tileSize; tx += tileSize)
                {
                    float cx = tx + ((ty / tileSize) % 2 == 0 ? tileSize / 2f : 0);
                    float cy = ty;

                    // Draw circular text
                    for (int i = 0; i < repeatPerCircle; i++)
                    {
                        float angle = 360f / repeatPerCircle * i;

                        canvas.Save();
                        canvas.Translate(cx, cy);
                        canvas.RotateDegrees(angle);
                        canvas.Translate(0, -radius);
                        
                        // Center text
                        canvas.DrawText(text, 0, textHeight/2, paint);
                        
                        canvas.Restore();
                    }
                }
            }
        }
        return bmp;
    }

    unsafe void GeometricDistortion(SKBitmap bmp, int strength, Random rng)
    {
        // Sinusoidal warp: x' = x + A * sin(freq * y + phase)
        // This breaks edge detection and grid-based feature extractors (ViT)
        
        SKBitmap copy = bmp.Copy();
        // Read from copy, write to bmp
        uint* srcPtr = (uint*)copy.GetPixels();
        uint* destPtr = (uint*)bmp.GetPixels();
        
        int w = bmp.Width;
        int h = bmp.Height;
        
        float ampX = strength * 0.5f; // Amplitude
        float freqX = 0.05f + (float)rng.NextDouble() * 0.1f; // Frequency
        float phaseX = (float)rng.NextDouble() * 10f;
        
        float ampY = strength * 0.5f;
        float freqY = 0.05f + (float)rng.NextDouble() * 0.1f;
        float phaseY = (float)rng.NextDouble() * 10f;

        for (int y = 0; y < h; y++)
        {
            // Vertical offset for this row
            // (Actually we should do pixel by pixel or row by row shift)
            // Just shifting pixels
            
            for (int x = 0; x < w; x++)
            {
                // Calculate source position
                float offX = ampX * (float)Math.Sin(freqX * y + phaseX);
                float offY = ampY * (float)Math.Cos(freqY * x + phaseY);
                
                int sx = ClampCoord(x + (int)offX, w);
                int sy = ClampCoord(y + (int)offY, h);
                
                destPtr[y * w + x] = srcPtr[sy * w + sx];
            }
        }
        
        copy.Dispose();
    }
    
    int ClampCoord(int v, int max)
    {
        if (v < 0) return 0;
        if (v >= max) return max - 1;
        return v;
    }

    unsafe void ColorShift(SKBitmap bmp, int strength, Random rng)
    {
        // Randomly shift RGB relationships in local blocks
        int w = bmp.Width;
        int h = bmp.Height;
        byte* ptr = (byte*)bmp.GetPixels();
        int len = w * h * 4;
        
        // Global shift factor
        int rShift = rng.Next(-strength, strength); // Blue shift actually (B G R A)
        int gShift = rng.Next(-strength, strength);
        int bShift = rng.Next(-strength, strength);

        for (int i = 0; i < len; i += 4)
        {
            // Skip alpha 0
            if (ptr[i+3] == 0) continue;
            
            // B
            ptr[i]   = (byte)Clamp(ptr[i] + rShift + rng.Next(-strength/2, strength/2)); 
            // G
            ptr[i+1] = (byte)Clamp(ptr[i+1] + gShift + rng.Next(-strength/2, strength/2));
            // R
            ptr[i+2] = (byte)Clamp(ptr[i+2] + bShift + rng.Next(-strength/2, strength/2));
        }
    }

    unsafe void AdversarialNoise(SKBitmap bmp, int strength, Random rng)
    {
        // Advanced Adversarial: Perceptual Masking
        // We inject strong noise only where the human eye detects "texture/edges" (High Variance),
        // and keep smooth areas (Low Variance) cleaner. AI relies on texture; we poison it there.
        
        int w = bmp.Width;
        int h = bmp.Height;
        byte* ptr = (byte*)bmp.GetPixels();
        
        int cellSize = 4;
        int w4 = w * 4;
        
        for (int y = 1; y < h; y++)
        {
            for (int x = 1; x < w; x++)
            {
                int idx = (y * w + x) * 4;
                if (ptr[idx + 3] == 0) continue;

                // 1. Calculate Local Variance
                // Use pointers for speed
                byte r = ptr[idx+2];
                byte g = ptr[idx+1];
                byte b = ptr[idx];
                
                int leftIdx = idx - 4;
                int upIdx = idx - w4;
                
                // Diff from Left
                int diffL = Math.Abs(r - ptr[leftIdx+2]) + Math.Abs(g - ptr[leftIdx+1]) + Math.Abs(b - ptr[leftIdx]);
                // Diff from Up
                int diffU = Math.Abs(r - ptr[upIdx+2]) + Math.Abs(g - ptr[upIdx+1]) + Math.Abs(b - ptr[upIdx]);
                
                int variance = (diffL + diffU) / 2;
                
                // 2. Modulate Strength
                // If high variance, we can hide a lot of noise.
                float localMult = 0.2f;
                if (variance > 10) localMult = 1.0f;
                if (variance > 40) localMult = 2.5f;
                
                int effectiveStrength = (int)(strength * localMult);

                // Checkerboard or Grid
                bool isGrid = (x % cellSize == 0) || (y % cellSize == 0);
                int cx = x / cellSize;
                int cy = y / cellSize;
                bool isCheck = (cx + cy) % 2 == 0;

                if (isGrid) {
                    // Darken slightly
                    int factor = (int)(-effectiveStrength * 0.5); 
                    ptr[idx] = (byte)Clamp(ptr[idx] + factor); // B
                    ptr[idx+1] = (byte)Clamp(ptr[idx+1] + factor); // G
                    ptr[idx+2] = (byte)Clamp(ptr[idx+2] + factor); // R
                } else if (isCheck) {
                    // Lighten or Color Noise
                    int factor = (int)(effectiveStrength * 0.4);
                    // Shift different channels differently
                    ptr[idx] = (byte)Clamp(ptr[idx] + factor); // B gets +
                    ptr[idx+1] = (byte)Clamp(ptr[idx+1] - factor); // G gets -
                    // R unchanged
                }
            }
        }
    }

    unsafe void SineInterference(SKBitmap bmp, int strength)
    {
        int w = bmp.Width;
        int h = bmp.Height;
        byte* ptr = (byte*)bmp.GetPixels();
        
        float period = 20f;
        float amp = strength * 0.8f;

        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                int idx = (y * w + x) * 4;
                if (ptr[idx + 3] == 0) continue;
                
                // Diagonal wave
                float wave = (float)Math.Sin((x + y) / period * Math.PI * 2) * amp;
                int iWave = (int)wave;
                
                ptr[idx] = (byte)Clamp(ptr[idx] + iWave);     // B
                ptr[idx+1] = (byte)Clamp(ptr[idx+1] + iWave); // G
                ptr[idx+2] = (byte)Clamp(ptr[idx+2] + iWave); // R
            }
        }
    }

    unsafe void BlockLocalScramble(SKBitmap bmp, int strength, Random rng)
    {
        int w = bmp.Width;
        int h = bmp.Height;
        byte* ptr = (byte*)bmp.GetPixels();

        int blockSize = Math.Min(6, Math.Max(2, (strength / 10) + 2));
        
        // Temp buffer for a block to avoid allocs? 
        // 6x6 * 4 bytes is tiny. stackalloc.
        // But strength is dynamic.
        int maxBlock = 16;
        uint* blockBuffer = stackalloc uint[maxBlock * maxBlock]; // max 16x16
        
        for (int by = 0; by < h; by += blockSize)
        {
            for (int bx = 0; bx < w; bx += blockSize)
            {
                // Collect
                int count = 0;
                for (int y = 0; y < blockSize; y++)
                {
                    if (by + y >= h) continue;
                    for (int x = 0; x < blockSize; x++)
                    {
                        if (bx + x >= w) continue;
                        
                        int idx = ((by + y) * w + (bx + x)) * 4;
                        // Read uint pixel
                        // Little endian: B G R A maps to uint
                        // Just mimic copy
                        uint pixel = *(uint*)(ptr + idx);
                        blockBuffer[count++] = pixel;
                    }
                }
                
                // Shuffle
                for (int i = count - 1; i > 0; i--)
                {
                    int j = rng.Next(0, i + 1);
                    uint temp = blockBuffer[i];
                    blockBuffer[i] = blockBuffer[j];
                    blockBuffer[j] = temp;
                }
                
                // Write back
                int writeIdx = 0;
                for (int y = 0; y < blockSize; y++)
                {
                    if (by + y >= h) continue;
                    for (int x = 0; x < blockSize; x++)
                    {
                        if (bx + x >= w) continue;
                        
                        int idx = ((by + y) * w + (bx + x)) * 4;
                        *(uint*)(ptr + idx) = blockBuffer[writeIdx++];
                    }
                }
            }
        }
    }
}
