import { useState, useRef, type ChangeEvent } from 'react';
import './App.css';
import { ProtectMode, processImage, drawWatermark } from './processor';

function App() {
  const [imageLoaded, setImageLoaded] = useState(false);
  const [processing, setProcessing] = useState(false);
  
  // State
  const [mode, setMode] = useState<ProtectMode>(ProtectMode.Balanced);
  const [strength, setStrength] = useState(25);
  const [useWatermark, setUseWatermark] = useState(false);
  const [watermarkText, setWatermarkText] = useState("DO NOT TRAIN");
  const [opacity, setOpacity] = useState(0.2);

  const canvasRef = useRef<HTMLCanvasElement>(null);
  const originalImageRef = useRef<HTMLImageElement | null>(null);

  // Initial load or resize logic could go here, but we rely on "Load Image"

  const handleImageUpload = (e: ChangeEvent<HTMLInputElement>) => {
    if (e.target.files && e.target.files[0]) {
      const file = e.target.files[0];
      const reader = new FileReader();
      
      reader.onload = (event) => {
        const img = new Image();
        img.onload = () => {
          originalImageRef.current = img;
          setImageLoaded(true);
          resetCanvas(img);
        };
        img.src = event.target?.result as string;
      };
      
      reader.readAsDataURL(file);
    }
  };

  const resetCanvas = (img: HTMLImageElement) => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    // Set canvas dimensions to image dimensions
    canvas.width = img.width;
    canvas.height = img.height;

    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    ctx.drawImage(img, 0, 0);
  };

  const handleApply = async () => {
    if (!originalImageRef.current || !canvasRef.current) return;
    
    setProcessing(true);

    // Allow UI to update before heavy processing
    setTimeout(() => {
      const img = originalImageRef.current!;
      const canvas = canvasRef.current!;
      const ctx = canvas.getContext('2d')!;

      // 1. Reset to original
      ctx.clearRect(0, 0, canvas.width, canvas.height);
      ctx.drawImage(img, 0, 0);

      // 2. Apply Noise/Protection
      // We use a random seed. JS simplified version used in processor.ts
      const seed = Math.floor(Math.random() * 10000);
      processImage(ctx, canvas.width, canvas.height, mode, strength, seed);

      // 3. Apply Watermark
      if (useWatermark) {
        drawWatermark(ctx, canvas.width, canvas.height, watermarkText, opacity);
      }

      setProcessing(false);
    }, 50);
  };

  const handleSave = () => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    const link = document.createElement('a');
    link.download = 'protected_image.png';
    link.href = canvas.toDataURL('image/png');
    link.click();
  };

  return (
    <div className="container" onDragOver={e => e.preventDefault()} onDrop={e => {
        e.preventDefault();
        // Handle drag and drop if desired, similar to input logic
    }}>
      <div className="sidebar">
        <div>
          <h1>GB Eater</h1>
          <p style={{ color: '#888', fontSize: '0.8rem', marginTop: '0.2rem' }}>
            Web Edition
          </p>
        </div>

        <div className="divider" />

        <div className="control-group">
          <label className="btn" style={{ position: 'relative', overflow: 'hidden' }}>
            📂 Load Image
            <input 
              type="file" 
              accept="image/*" 
              onChange={handleImageUpload} 
              className="sr-only"
            />
          </label>
          
          <button 
            className="btn" 
            onClick={handleSave} 
            disabled={!imageLoaded}
          >
            💾 Save Image
          </button>
        </div>

        <div className="divider" />

        <div className="control-group">
          <label>Protection Mode</label>
          <select 
            value={mode} 
            onChange={(e) => setMode(Number(e.target.value) as ProtectMode)}
          >
            <option value={ProtectMode.Soft}>Soft</option>
            <option value={ProtectMode.Balanced}>Balanced</option>
            <option value={ProtectMode.Strong}>Strong</option>
          </select>
        </div>

        <div className="control-group">
          <label>
            Strength
            <span>{strength}</span>
          </label>
          <input 
            type="range" 
            min="0" 
            max="50" 
            value={strength} 
            onChange={(e) => setStrength(Number(e.target.value))} 
          />
        </div>

        <div className="divider" />

        <div className="control-group">
          <label className="switch-wrapper">
            <input 
              type="checkbox" 
              className="sr-only"
              checked={useWatermark} 
              onChange={(e) => setUseWatermark(e.target.checked)} 
            />
            <span className="checkbox-visual"></span>
            Add Watermark
          </label>
        </div>

        {useWatermark && (
          <>
            <div className="control-group">
              <input 
                type="text" 
                value={watermarkText} 
                onChange={(e) => setWatermarkText(e.target.value)} 
                placeholder="Watermark Text"
              />
            </div>
            
            <div className="control-group">
              <label>
                Opacity
                <span>{Math.round(opacity * 100)}%</span>
              </label>
              <input 
                type="range" 
                min="0" 
                max="1" 
                step="0.01" 
                value={opacity} 
                onChange={(e) => setOpacity(Number(e.target.value))} 
              />
            </div>
          </>
        )}

        <div className="divider" />

        <button 
          className="btn btn-primary" 
          onClick={handleApply}
          disabled={!imageLoaded || processing}
        >
          {processing ? 'Processing...' : '⚡ PREVIEW & APPLY'}
        </button>

        <p style={{ fontSize: '0.75rem', color: '#666', textAlign: 'center' }}>
          Processing happens locally. Large images may take a moment.
        </p>

      </div>

      <div className="preview-area">
        {!imageLoaded && (
            <div style={{ color: '#444', textAlign: 'center' }}>
                <p style={{ fontSize: '3rem', marginBottom: '1rem' }}>🖼️</p>
                <p>Load an image to start</p>
            </div>
        )}
        <canvas 
          ref={canvasRef} 
          className="preview-canvas"
          style={{ display: imageLoaded ? 'block' : 'none' }}
        />
      </div>
    </div>
  );
}

export default App;
