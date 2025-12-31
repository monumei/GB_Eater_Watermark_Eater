using SkiaSharp;
using System;
using Avalonia;

namespace GB_Eater.Application
{
    public enum ProtectMode { Soft = 0, Balanced = 1, Strong = 2, AIPoison = 3 }

    public static class ImageProcessor
    {
        public static SKBitmap ProtectImage(SKBitmap src, int strength, ProtectMode mode)
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

        private static int Clamp(int v) => Math.Max(0, Math.Min(255, v));

        private static unsafe void ApplyBalancedNoise(SKBitmap bmp, int strength, Random rng)
        {
            int w = bmp.Width;
            int h = bmp.Height;
            
            byte* ptr = (byte*)bmp.GetPixels();
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
            }
        }

        private static unsafe void TextureNoise(SKBitmap bmp, int strength, Random rng)
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

        private static unsafe void EdgeJitter(SKBitmap bmp, int strength)
        {
            SKBitmap copy = bmp.Copy();
            uint* srcPtr = (uint*)bmp.GetPixels(); // Read from current (Wait, logic was copy pixels manually?)
            // In original code: copy = bmp.Copy(); srcPtr = bmp.GetPixels(); destPtr = copy.GetPixels();
            // Then it does: destPtr[destOffset] = srcPtr[srcOffset];
            // Then writes copy back to parameters? No, Buffer.MemoryCopy(destPtr, srcPtr...)
            // So it writes the jittered version (in copy) back to bmp (srcPtr).
            
            // Replicating original logic exactly:
            uint* destPtr = (uint*)copy.GetPixels(); 
            
            // Note: In original code, srcPtr was from `bmp` and destPtr from `copy`.
            // Wait, logic: `destPtr[destOffset] = srcPtr[srcOffset];`
            // Then `Buffer.MemoryCopy(destPtr, srcPtr, ...)` -> Copy FROM dest TO src.
            // So `copy` holds the Modified image, and we overwrite `bmp`.
            // Correct.
            
            int w = bmp.Width;
            int h = bmp.Height;

            for (int y = 1; y < h - 1; y++)
            {
                for (int x = 1; x < w - 1; x++)
                {
                    if ((x + y) % 4 != 0) continue;

                    int srcOffset = y * w + x;
                    int destOffset = y * w + (x + 1);
                    
                    destPtr[destOffset] = srcPtr[srcOffset]; 
                }
            }
            
            Buffer.MemoryCopy(destPtr, srcPtr, w * h * 4, w * h * 4);
            copy.Dispose();
        }

        private static unsafe void GeometricDistortion(SKBitmap bmp, int strength, Random rng)
        {
            SKBitmap copy = bmp.Copy();
            uint* srcPtr = (uint*)copy.GetPixels(); // Read from COPY
            uint* destPtr = (uint*)bmp.GetPixels(); // Write to BMP
            
            int w = bmp.Width;
            int h = bmp.Height;
            
            float ampX = strength * 0.5f;
            float freqX = 0.05f + (float)rng.NextDouble() * 0.1f;
            float phaseX = (float)rng.NextDouble() * 10f;
            
            float ampY = strength * 0.5f;
            float freqY = 0.05f + (float)rng.NextDouble() * 0.1f;
            float phaseY = (float)rng.NextDouble() * 10f;

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    float offX = ampX * (float)Math.Sin(freqX * y + phaseX);
                    float offY = ampY * (float)Math.Cos(freqY * x + phaseY);
                    
                    int sx = ClampCoord(x + (int)offX, w);
                    int sy = ClampCoord(y + (int)offY, h);
                    
                    destPtr[y * w + x] = srcPtr[sy * w + sx];
                }
            }
            
            copy.Dispose();
        }
        
        private static int ClampCoord(int v, int max)
        {
            if (v < 0) return 0;
            if (v >= max) return max - 1;
            return v;
        }

        private static unsafe void ColorShift(SKBitmap bmp, int strength, Random rng)
        {
            int w = bmp.Width;
            int h = bmp.Height;
            byte* ptr = (byte*)bmp.GetPixels();
            int len = w * h * 4;
            
            int rShift = rng.Next(-strength, strength);
            int gShift = rng.Next(-strength, strength);
            int bShift = rng.Next(-strength, strength);

            for (int i = 0; i < len; i += 4)
            {
                if (ptr[i+3] == 0) continue;
                
                ptr[i]   = (byte)Clamp(ptr[i] + rShift + rng.Next(-strength/2, strength/2)); 
                ptr[i+1] = (byte)Clamp(ptr[i+1] + gShift + rng.Next(-strength/2, strength/2));
                ptr[i+2] = (byte)Clamp(ptr[i+2] + bShift + rng.Next(-strength/2, strength/2));
            }
        }

        private static unsafe void AdversarialNoise(SKBitmap bmp, int strength, Random rng)
        {
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

                    byte r = ptr[idx+2];
                    byte g = ptr[idx+1];
                    byte b = ptr[idx];
                    
                    int leftIdx = idx - 4;
                    int upIdx = idx - w4;
                    
                    int diffL = Math.Abs(r - ptr[leftIdx+2]) + Math.Abs(g - ptr[leftIdx+1]) + Math.Abs(b - ptr[leftIdx]);
                    int diffU = Math.Abs(r - ptr[upIdx+2]) + Math.Abs(g - ptr[upIdx+1]) + Math.Abs(b - ptr[upIdx]);
                    
                    int variance = (diffL + diffU) / 2;
                    
                    float localMult = 0.2f;
                    if (variance > 10) localMult = 1.0f;
                    if (variance > 40) localMult = 2.5f;
                    
                    int effectiveStrength = (int)(strength * localMult);

                    bool isGrid = (x % cellSize == 0) || (y % cellSize == 0);
                    int cx = x / cellSize;
                    int cy = y / cellSize;
                    bool isCheck = (cx + cy) % 2 == 0;

                    if (isGrid) {
                        int factor = (int)(-effectiveStrength * 0.5); 
                        ptr[idx] = (byte)Clamp(ptr[idx] + factor); 
                        ptr[idx+1] = (byte)Clamp(ptr[idx+1] + factor); 
                        ptr[idx+2] = (byte)Clamp(ptr[idx+2] + factor); 
                    } else if (isCheck) {
                        int factor = (int)(effectiveStrength * 0.4);
                        ptr[idx] = (byte)Clamp(ptr[idx] + factor);
                        ptr[idx+1] = (byte)Clamp(ptr[idx+1] - factor);
                    }
                }
            }
        }

        private static unsafe void SineInterference(SKBitmap bmp, int strength)
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
                    
                    float wave = (float)Math.Sin((x + y) / period * Math.PI * 2) * amp;
                    int iWave = (int)wave;
                    
                    ptr[idx] = (byte)Clamp(ptr[idx] + iWave);
                    ptr[idx+1] = (byte)Clamp(ptr[idx+1] + iWave);
                    ptr[idx+2] = (byte)Clamp(ptr[idx+2] + iWave);
                }
            }
        }

        private static unsafe void BlockLocalScramble(SKBitmap bmp, int strength, Random rng)
        {
            int w = bmp.Width;
            int h = bmp.Height;
            byte* ptr = (byte*)bmp.GetPixels();

            int blockSize = Math.Min(6, Math.Max(2, (strength / 10) + 2));
            int maxBlock = 16;
            uint* blockBuffer = stackalloc uint[maxBlock * maxBlock]; // max 16x16
            
            for (int by = 0; by < h; by += blockSize)
            {
                for (int bx = 0; bx < w; bx += blockSize)
                {
                    int count = 0;
                    for (int y = 0; y < blockSize; y++)
                    {
                        if (by + y >= h) continue;
                        for (int x = 0; x < blockSize; x++)
                        {
                            if (bx + x >= w) continue;
                            
                            int idx = ((by + y) * w + (bx + x)) * 4;
                            uint pixel = *(uint*)(ptr + idx);
                            blockBuffer[count++] = pixel;
                        }
                    }
                    
                    for (int i = count - 1; i > 0; i--)
                    {
                        int j = rng.Next(0, i + 1);
                        uint temp = blockBuffer[i];
                        blockBuffer[i] = blockBuffer[j];
                        blockBuffer[j] = temp;
                    }
                    
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
}
