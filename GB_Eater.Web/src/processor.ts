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
      
      // 1. Balanced (High freq noise)
      applyBalancedNoise(data, strength, nextRandom);
      
      // 2. Geometric Distortion
      // Need to write to a temp buffer or copy
      const copy = new Uint8ClampedArray(data);
      geometricDistortion(data, copy, width, height, strength, nextRandomFloat);
      
      // 3. Color Shift
      colorShift(data, strength, nextRandom);
      
      // 4. Heavy Texture
      textureNoise(data, width, height, strength, nextRandom);
      
      ctx.putImageData(imageData, 0, 0);
      return;
  }

  // 1. Balanced Noise (Soft, Balanced, Strong)
  // C# Logic:
  // val = 0.299r + ...
  // noise = rand(-strength, strength)
  // ratio = (val+noise)/val
  // r *= ratio, etc.

  const noiseStrength =
    mode === ProtectMode.Soft ? Math.floor(strength / 2) : strength;

  applyBalancedNoise(data, noiseStrength, nextRandom);

  // 2. Edge Jitter (Balanced, Strong)
  if (mode >= ProtectMode.Balanced) {
    // jitterStrength removed as it was unused logic in original C# too
    // Original C# passed "strength/2" or "strength".
    // But the C# EdgeJitter implementation loop:
    // if ((x+y)%4 != 0) continue
    // Move pixel (x,y) to (x+1, y)
    // It didn't actually use 'strength' inside the loop logic?
    // Wait, looking at C# code: `Bitmap EdgeJitter(Bitmap src, int strength)`
    // `if ((x + y) % 4 != 0) continue;`
    // `Color c = bmp.GetPixel(x, y); bmp.SetPixel(x + 1, y, c);`
    // It completely ignored the `strength` parameter! It just did a fixed visual glitch.
    // We will replicate that.

    // We need a copy to read from because we are shifting.
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

  // 3. Texture Noise (Strong only)
  if (mode === ProtectMode.Strong) {
    const texStrength = Math.floor(strength / 2);
    textureNoise(data, width, height, texStrength, nextRandom);
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
        
        data[i] = clamp(data[i] + rShift + nextRandom(-5, 5)); // R (actually JS is R,G,B order, C# was B,G,R)
        // Wait, Canvas ImageData is RGBA. C# Skia was BGRA.
        // My colorShift logic in C# named variables rShift but applied to ptr[i] which was B.
        // So rShift was shifting B. 
        // We will just shift R, G, B channels here randomly.
        
        data[i+1] = clamp(data[i+1] + gShift + nextRandom(-5, 5));
        data[i+2] = clamp(data[i+2] + bShift + nextRandom(-5, 5));
    }
}

export function drawWatermark(
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
