import { useState, useRef, type ChangeEvent } from 'react';
import './App.css';
import { ProtectMode, processImage } from './processor';

function App() {
  const [imageLoaded, setImageLoaded] = useState(false);
  const [processing, setProcessing] = useState(false);
  
  // State
  const [mode, setMode] = useState<ProtectMode>(ProtectMode.Balanced);
  const [strength, setStrength] = useState(25);
  const [useWatermark, setUseWatermark] = useState(false);
  const [opacity, setOpacity] = useState(0.2);

  const canvasRef = useRef<HTMLCanvasElement>(null);
  const originalImageRef = useRef<HTMLImageElement | null>(null);
  
  // New State for Image Watermark
  const [watermarkImg, setWatermarkImg] = useState<HTMLImageElement | null>(null);
  const [watermarkPos, setWatermarkPos] = useState({ x: 50, y: 50 });
  const [isDragging, setIsDragging] = useState(false);
  const dragOffset = useRef({ x: 0, y: 0 });
  
  // Cache processed background so we don't re-run protecting on drag
  const processedDataRef = useRef<ImageData | null>(null);

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
          processedDataRef.current = null; // Reset cache
          resetCanvas(img);
        };
        img.src = event.target?.result as string;
      };
      
      reader.readAsDataURL(file);
    }
  };

  const handleWatermarkImageUpload = (e: ChangeEvent<HTMLInputElement>) => {
    if (e.target.files && e.target.files[0]) {
      const file = e.target.files[0];
      const reader = new FileReader();
      
      reader.onload = (event) => {
        const img = new Image();
        img.onload = () => {
          setWatermarkImg(img);
          setUseWatermark(true);
          // Default pos
          setWatermarkPos({ x: (canvasRef.current?.width || 500) / 2 - img.width/2, y: (canvasRef.current?.height || 500) / 2 - img.height/2 });
          requestAnimationFrame(renderCanvas);
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

  const renderCanvas = () => {
      const canvas = canvasRef.current;
      if (!canvas) return;
      const ctx = canvas.getContext('2d');
      if (!ctx) return;
      
      // 1. Draw Background (Processed or Original)
      if (processedDataRef.current) {
          ctx.putImageData(processedDataRef.current, 0, 0);
      } else if (originalImageRef.current) {
          // If no processed data, just draw original
          ctx.drawImage(originalImageRef.current, 0, 0, canvas.width, canvas.height);
      }
      
      
      // 2. Tiled Text Watermark - REMOVED per user request
      
      // 3. Image Watermark
      if (useWatermark && watermarkImg) {
          ctx.save();
          ctx.globalAlpha = opacity;
          ctx.drawImage(watermarkImg, watermarkPos.x, watermarkPos.y);
          ctx.restore();
          
          if (useWatermark && watermarkImg) {
             // Draw border if dragging or hovering? Maybe just simple for now.
          }
      }
  };

  const handleApply = async () => {
    if (!originalImageRef.current || !canvasRef.current) return;
    
    setProcessing(true);

    setTimeout(() => {
      const img = originalImageRef.current!;
      const canvas = canvasRef.current!;
      const ctx = canvas.getContext('2d')!;

      // 1. Reset to original
      ctx.clearRect(0, 0, canvas.width, canvas.height);
      ctx.drawImage(img, 0, 0);

      // 2. Apply Noise/Protection
      const seed = Math.floor(Math.random() * 10000);
      processImage(ctx, canvas.width, canvas.height, mode, strength, seed);
      
      // Cache the result
      processedDataRef.current = ctx.getImageData(0, 0, canvas.width, canvas.height);

      // 3. Render Watermarks
      renderCanvas();

      setProcessing(false);
    }, 50);
  };
  
  // Canvas Interaction
  const getCanvasCoordinates = (e: React.MouseEvent | React.TouchEvent) => {
      const canvas = canvasRef.current;
      if (!canvas) return { x: 0, y: 0 };
      
      const rect = canvas.getBoundingClientRect();
      const scaleX = canvas.width / rect.width;
      const scaleY = canvas.height / rect.height;
      
      let clientX, clientY;
      if ('touches' in e) {
          clientX = e.touches[0].clientX;
          clientY = e.touches[0].clientY;
      } else {
          clientX = (e as React.MouseEvent).clientX;
          clientY = (e as React.MouseEvent).clientY;
      }
      
      return {
          x: (clientX - rect.left) * scaleX,
          y: (clientY - rect.top) * scaleY
      };
  };

  const handleMouseDown = (e: React.MouseEvent | React.TouchEvent) => {
      if (!useWatermark || !watermarkImg) return;
      
      const { x, y } = getCanvasCoordinates(e);
      
      // Check hit
      const w = watermarkImg.width;
      const h = watermarkImg.height;
      if (x >= watermarkPos.x && x <= watermarkPos.x + w &&
          y >= watermarkPos.y && y <= watermarkPos.y + h) {
          
          setIsDragging(true);
          dragOffset.current = { x: x - watermarkPos.x, y: y - watermarkPos.y };
      }
  };
  
  const handleMouseMove = (e: React.MouseEvent | React.TouchEvent) => {
      if (!isDragging || !watermarkImg) return;
      e.preventDefault(); // Prevent scrolling on touch
      
      const { x, y } = getCanvasCoordinates(e);
      setWatermarkPos({
          x: x - dragOffset.current.x,
          y: y - dragOffset.current.y
      });
      
      requestAnimationFrame(renderCanvas);
  };
  
  const handleMouseUp = () => {
      setIsDragging(false);
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
            <option value={ProtectMode.AIPoison}>AI Poison (Slow)</option>
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
               <label className="btn" style={{ position: 'relative', overflow: 'hidden', marginTop: '10px' }}>
                 Upload Signature/Logo
                 <input 
                   type="file" 
                   accept="image/*" 
                   onChange={handleWatermarkImageUpload} 
                   className="sr-only"
                 />
               </label>
               {watermarkImg && <button onClick={() => { setWatermarkImg(null); requestAnimationFrame(renderCanvas); }} style={{fontSize:'0.8em', marginTop:'5px'}}>Remove Image</button>}
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
                onChange={(e) => {
                    setOpacity(Number(e.target.value));
                    requestAnimationFrame(renderCanvas);
                }} 
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
          style={{ display: imageLoaded ? 'block' : 'none', cursor: (useWatermark && watermarkImg) ? 'move' : 'default' }}
          onMouseDown={handleMouseDown}
          onMouseMove={handleMouseMove}
          onMouseUp={handleMouseUp}
          onMouseLeave={handleMouseUp}
          onTouchStart={handleMouseDown}
          onTouchMove={handleMouseMove}
          onTouchEnd={handleMouseUp}
        />
      </div>
    </div>
  );
}

export default App;
