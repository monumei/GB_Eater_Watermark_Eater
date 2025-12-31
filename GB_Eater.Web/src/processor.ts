export const ProtectMode = {
    Soft: 0,
    Balanced: 1,
    Strong: 2,
    AIPoison: 3
} as const;

export type ProtectMode = typeof ProtectMode[keyof typeof ProtectMode];

function clamp(v: number): number {
  return Math.max(0, Math.min(255, v));
}

export function processImage(
  ctx: CanvasRenderingContext2D,
  width: number,
  height: number,
  mode: ProtectMode,
  strength: number,
  seed: number
) {
  const imageData = ctx.getImageData(0, 0, width, height); 
  const data = imageData.data;

  // Helper to get random numbers (simple seedable LCG)
  let currentSeed = seed;
  const nextRandom = (min: number, max: number) => {
    currentSeed = (currentSeed * 9301 + 49297) % 233280;
    const rnd = currentSeed / 233280;
    return Math.floor(min + rnd * (max - min + 1));
  };
  
  // Helper for float random
  const nextRandomFloat = () => {
    currentSeed = (currentSeed * 9301 + 49297) % 233280;
    return currentSeed / 233280;
  };

  if (mode === ProtectMode.AIPoison) {
      // 0. Heavy Texture first (reusing logic later? No, order matters)
      // C# AIPoison: Balanced -> Geometric -> Color -> Texture
      
      // 1. Sine Interference (Global frequency disruption)
      sineInterference(data, width, height, strength * 0.5);

      // 2. Geometric Distortion (Maximized)
      const copy = new Uint8ClampedArray(data);
      geometricDistortion(data, copy, width, height, strength * 0.5, nextRandomFloat);
      
      // 3. Adversarial Pattern
      adversarialNoise(data, width, height, strength * 0.8, nextRandom);

      // 4. Color Shift (Boosted)
      colorShift(data, strength * 0.8, nextRandom);
      
      // 5. Block Local Scramble (Destroys local gradients)
      blockLocalScramble(data, width, height, strength * 0.5, nextRandom);
      
      ctx.putImageData(imageData, 0, 0);
      return;
  }

  // 1. Balanced Noise (Soft, Balanced, Strong)
  // C# Logic:
  // val = 0.299r + ...
  // noise = rand(-strength, strength)
  // ratio = (val+noise)/val
  // r *= ratio, etc.

  // 1. Balanced Noise (Soft, Balanced, Strong)
  const noiseStrength =
    mode === ProtectMode.Soft ? Math.floor(strength / 2) : strength;

  applyBalancedNoise(data, noiseStrength, nextRandom);

  // New: Chroma Noise for Soft
  if (mode === ProtectMode.Soft) {
      colorShift(data, Math.floor(strength / 4), nextRandom);
  }

  // 2. Edge Jitter & Adversarial (Balanced, Strong)
  if (mode >= ProtectMode.Balanced) {
    
    // New: Weak Adversarial Grid
    adversarialNoise(data, width, height, Math.floor(strength / 3), nextRandom);

    // Edge Jitter (Logic mostly unchanged)
    // jitterStrength removed as it was unused logic in original C# too
    const originalData = new Uint8ClampedArray(data);

    for (let y = 1; y < height - 1; y++) {
      for (let x = 1; x < width - 1; x++) {
        if ((x + y) % 4 !== 0) continue;

        const srcIdx = (y * width + x) * 4;
        const destIdx = (y * width + (x + 1)) * 4;

        data[destIdx] = originalData[srcIdx];
        data[destIdx + 1] = originalData[srcIdx + 1];
        data[destIdx + 2] = originalData[srcIdx + 2];
        data[destIdx + 3] = originalData[srcIdx + 3];
      }
    }
  }

  // 3. Texture, Color, Geom (Strong only)
  if (mode === ProtectMode.Strong) {
    const texStrength = Math.floor(strength / 2);
    textureNoise(data, width, height, texStrength, nextRandom);
    
    // New: Boost Strong mode
    colorShift(data, Math.floor(strength / 2), nextRandom);
    const copy = new Uint8ClampedArray(data);
    geometricDistortion(data, copy, width, height, Math.floor(strength / 5), nextRandomFloat);
  }

  ctx.putImageData(imageData, 0, 0);
}

// === Extracted Functions ===

function applyBalancedNoise(data: Uint8ClampedArray, strength: number, nextRandom: (min:number, max:number)=>number) {
    for (let i = 0; i < data.length; i += 4) {
        if (data[i + 3] === 0) continue;
    
        const r = data[i];
        const g = data[i + 1];
        const b = data[i + 2];
    
        const lum = 0.299 * r + 0.587 * g + 0.114 * b;
        const noise = nextRandom(-strength, strength);
        const newLum = clamp(lum + noise);
    
        const ratio = lum === 0 ? 1 : newLum / lum;
    
        data[i] = clamp(r * ratio);
        data[i + 1] = clamp(g * ratio);
        data[i + 2] = clamp(b * ratio);
    }
}

function textureNoise(data: Uint8ClampedArray, width: number, height: number, strength: number, nextRandom: (min:number, max:number)=>number) {
    for (let y = 0; y < height; y++) {
      for (let x = 0; x < width; x++) {
        if ((x + y) % 3 !== 0) continue;

        const idx = (y * width + x) * 4;
        if (data[idx + 3] === 0) continue;

        const n = nextRandom(-strength, strength);

        data[idx] = clamp(data[idx] + n);
        data[idx + 1] = clamp(data[idx + 1] + n);
        data[idx + 2] = clamp(data[idx + 2] + n);
      }
    }
}

function geometricDistortion(
    target: Uint8ClampedArray, 
    source: Uint8ClampedArray, 
    width: number, 
    height: number, 
    strength: number, 
    nextFloat: ()=>number
) {
    const ampX = strength * 0.5;
    const freqX = 0.05 + nextFloat() * 0.1;
    const phaseX = nextFloat() * 10;
    
    const ampY = strength * 0.5;
    const freqY = 0.05 + nextFloat() * 0.1;
    const phaseY = nextFloat() * 10;
    
    for (let y = 0; y < height; y++) {
        for (let x = 0; x < width; x++) {
            const offX = ampX * Math.sin(freqX * y + phaseX);
            const offY = ampY * Math.cos(freqY * x + phaseY);
            
            let sx = x + Math.floor(offX);
            let sy = y + Math.floor(offY);
            
            // Clamp coord
            if (sx < 0) sx = 0;
            if (sx >= width) sx = width - 1;
            if (sy < 0) sy = 0;
            if (sy >= height) sy = height - 1;
            
            const srcIdx = (sy * width + sx) * 4;
            const destIdx = (y * width + x) * 4;
            
            target[destIdx] = source[srcIdx];
            target[destIdx + 1] = source[srcIdx + 1];
            target[destIdx + 2] = source[srcIdx + 2];
            target[destIdx + 3] = source[srcIdx + 3];
        }
    }
}

function colorShift(data: Uint8ClampedArray, strength: number, nextRandom: (min:number, max:number)=>number) {
    const rShift = nextRandom(-strength, strength);
    const gShift = nextRandom(-strength, strength);
    const bShift = nextRandom(-strength, strength);
    
    for (let i = 0; i < data.length; i += 4) {
        if (data[i+3] === 0) continue;
        
        // R
        data[i] = clamp(data[i] + rShift + nextRandom(-strength/2, strength/2)); 
        // G
        data[i+1] = clamp(data[i+1] + gShift + nextRandom(-strength/2, strength/2));
        // B
        data[i+2] = clamp(data[i+2] + bShift + nextRandom(-strength/2, strength/2));
    }
}

function adversarialNoise(data: Uint8ClampedArray, width: number, height: number, strength: number, nextRandom: (min:number, max:number)=>number) {
    // Advanced Adversarial: Perceptual Masking
    // We inject strong noise only where the human eye detects "texture/edges" (High Variance),
    // and keep smooth areas (Low Variance) cleaner. AI relies on texture; we poison it there.
    
    const cellSize = 4;
    
    // Pre-calculate luminance for speed or just do simple RGB diff
    const w4 = width * 4;

    for (let y = 1; y < height; y++) {
        for (let x = 1; x < width; x++) {
             const idx = (y * width + x) * 4;
             if (data[idx + 3] === 0) continue;

             // 1. Calculate Local Variance (Edge Detection)
             // Simple delta from Left and Up neighbors
             // |Current - Left| + |Current - Up|
             const r = data[idx]; const g = data[idx+1]; const b = data[idx+2];
             
             const leftIdx = idx - 4;
             const upIdx = idx - w4;
             
             const rL = data[leftIdx]; const gL = data[leftIdx+1]; const bL = data[leftIdx+2];
             const rU = data[upIdx];   const gU = data[upIdx+1];   const bU = data[upIdx+2];

             const diffL = Math.abs(r - rL) + Math.abs(g - gL) + Math.abs(b - bL);
             const diffU = Math.abs(r - rU) + Math.abs(g - gU) + Math.abs(b - bU);
             
             // Variance score (0 to ~1500 typically).
             // If variance is high (>30), we are in a texture/edge. 
             const variance = (diffL + diffU) / 2;
             
             // 2. Modulate Strength
             // Base strength: 20%
             // Boosted strength: up to 200% if high variance
             let localMult = 0.2;
             if (variance > 10) localMult = 1.0;
             if (variance > 40) localMult = 2.5; // Hide heavy noise in chaos
             
             const effectiveStrength = strength * localMult;

             // 3. Grid Pattern Injection
             const isGrid = (x % cellSize === 0) || (y % cellSize === 0);
             const cx = Math.floor(x / cellSize);
             const cy = Math.floor(y / cellSize);
             const isCheck = (cx + cy) % 2 === 0;

             if (isGrid) {
                 // Darken
                 const factor = -effectiveStrength * 0.5; 
                 data[idx] = clamp(data[idx] + factor);
                 data[idx+1] = clamp(data[idx+1] + factor);
                 data[idx+2] = clamp(data[idx+2] + factor);
             } else if (isCheck) {
                 // Color Scramble
                 const factor = effectiveStrength * 0.4;
                 data[idx] = clamp(data[idx] + factor); // R boosted
                 data[idx+1] = clamp(data[idx+1] - factor); // G reduced
                 // B unchanged or noisy
             }
        }
    }
}

export default function drawWatermark(
  ctx: CanvasRenderingContext2D,
  width: number,
  height: number,
  text: string,
  opacity: number
) {
  const tileSize = 300;
  const repeatPerCircle = 16;
  const radius = tileSize * 0.35;

  ctx.save();
  ctx.font = `bold ${Math.max(12, width / 40)}px Arial`;
  ctx.textAlign = "center";
  ctx.textBaseline = "middle";
  ctx.fillStyle = `rgba(255, 255, 255, ${opacity})`;

  for (let ty = 0; ty < height + tileSize; ty += tileSize) {
    for (let tx = 0; tx < width + tileSize; tx += tileSize) {
      // Offset every other row
      const cx = tx + (Math.floor(ty / tileSize) % 2 === 0 ? tileSize / 2 : 0);
      const cy = ty;

      for (let i = 0; i < repeatPerCircle; i++) {
        const angle = ((Math.PI * 2) / repeatPerCircle) * i;

        ctx.save();
        ctx.translate(cx, cy);
        ctx.rotate(angle);
        ctx.translate(0, -radius);
        ctx.fillText(text, 0, 0);
        ctx.restore();
      }
    }
  }
  ctx.restore();
}

function sineInterference(data: Uint8ClampedArray, width: number, height: number, strength: number) {
    const period = 20; // Pixels per wave
    const amp = strength * 0.8;
    
    for (let y = 0; y < height; y++) {
        for (let x = 0; x < width; x++) {
            const idx = (y * width + x) * 4;
            if (data[idx+3] === 0) continue;
            
            // Diagonal wave
            const wave = Math.sin((x + y) / period * Math.PI * 2) * amp;
            
            data[idx] = clamp(data[idx] + wave);
            data[idx+1] = clamp(data[idx+1] + wave);
            data[idx+2] = clamp(data[idx+2] + wave);
        }
    }
}

function blockLocalScramble(data: Uint8ClampedArray, width: number, height: number, strength: number, nextRandom: (min:number, max:number)=>number) {
    // Scramble pixels within NxN blocks
    // Higher strength -> Larger blocks (up to 4 or 5)
    // This destroys CNN kernel features
    const blockSize = Math.min(6, Math.max(2, Math.floor(strength / 10) + 2)); 
    
    // We process block by block
    for (let by = 0; by < height; by += blockSize) {
        for (let bx = 0; bx < width; bx += blockSize) {
            
            // Collect pixels in this block
            const pixels: number[] = [];
            const coords: number[] = [];
            
            for (let y = 0; y < blockSize; y++) {
                if (by + y >= height) continue;
                for (let x = 0; x < blockSize; x++) {
                    if (bx + x >= width) continue;
                    
                    const idx = ((by + y) * width + (bx + x)) * 4;
                    coords.push(idx);
                    pixels.push(data[idx], data[idx+1], data[idx+2], data[idx+3]);
                }
            }
            
            // Shuffle
            // Fisher-Yates inside the block
            // Note: We shuffle entire pixels (RGBA group)
            const count = coords.length;
            
            // Create permutation
            const perm = new Uint32Array(count);
            for(let i=0; i<count; i++) perm[i] = i;
            
            // Shuffle logic
            for (let i = count - 1; i > 0; i--) {
                const j = Math.floor(nextRandom(0, i)); // 0 to i
                const temp = perm[i];
                perm[i] = perm[j];
                perm[j] = temp;
            }
            
            // Apply back
            for (let i = 0; i < count; i++) {
                const destIdx = coords[i];
                const srcIdx = perm[i] * 4; // *4 because 'pixels' array is flat byte array
                
                data[destIdx] = pixels[srcIdx];
                data[destIdx+1] = pixels[srcIdx+1];
                data[destIdx+2] = pixels[srcIdx+2];
                data[destIdx+3] = pixels[srcIdx+3];
            }
        }
    }
}
