export const ProtectMode = {
    Soft: 0,
    Balanced: 1,
    Strong: 2
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

  // Helper to get random numbers (simple seedable LCG for consistency if needed, or just Math.random)
  // C# used a seed. JS Math.random is not seedable.
  // We'll implement a simple one to match the "Random rng = new Random(...)" logic
  let currentSeed = seed;
  const nextRandom = (min: number, max: number) => {
    currentSeed = (currentSeed * 9301 + 49297) % 233280;
    const rnd = currentSeed / 233280;
    // range [min, max]
    return Math.floor(min + rnd * (max - min + 1));
  };

  // 1. Balanced Noise (Soft, Balanced, Strong)
  // C# Logic:
  // val = 0.299r + ...
  // noise = rand(-strength, strength)
  // ratio = (val+noise)/val
  // r *= ratio, etc.

  const noiseStrength =
    mode === ProtectMode.Soft ? Math.floor(strength / 2) : strength;

  for (let i = 0; i < data.length; i += 4) {
    // Skip alpha 0
    if (data[i + 3] === 0) continue;

    const r = data[i];
    const g = data[i + 1];
    const b = data[i + 2];

    const lum = 0.299 * r + 0.587 * g + 0.114 * b;
    const noise = nextRandom(-noiseStrength, noiseStrength);
    const newLum = clamp(lum + noise);

    const ratio = lum === 0 ? 1 : newLum / lum;

    data[i] = clamp(r * ratio);
    data[i + 1] = clamp(g * ratio);
    data[i + 2] = clamp(b * ratio);
  }

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

    for (let y = 0; y < height; y++) {
      for (let x = 0; x < width; x++) {
        if ((x + y) % 3 !== 0) continue;

        const idx = (y * width + x) * 4;
        if (data[idx + 3] === 0) continue;

        const n = nextRandom(-texStrength, texStrength);

        data[idx] = clamp(data[idx] + n);
        data[idx + 1] = clamp(data[idx + 1] + n);
        data[idx + 2] = clamp(data[idx + 2] + n);
      }
    }
  }

  ctx.putImageData(imageData, 0, 0);
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
