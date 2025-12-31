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
  const [watermarkScale, setWatermarkScale] = useState(1.0);
  const [isDragging, setIsDragging] = useState(false);
  const [isPanning, setIsPanning] = useState(false);
  const dragOffset = useRef({ x: 0, y: 0 });
  
  // View Transform
  const [viewTransform, setViewTransform] = useState({ scale: 1, x: 0, y: 0 });
  const lastMousePos = useRef({ x: 0, y: 0 }); // For panning delta

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
      
      // Clear
      ctx.clearRect(0, 0, canvas.width, canvas.height);
      
      ctx.save();
      // Apply View Transform
      ctx.translate(viewTransform.x, viewTransform.y);
      ctx.scale(viewTransform.scale, viewTransform.scale);
      
      // 1. Draw Background (Processed or Original)
      if (processedDataRef.current) {
          ctx.putImageData(processedDataRef.current, 0, 0); // putImageData ignores context transform!
          // Issue: putImageData places pixels directly. It does NOT respect scale/translate.
          // Solution: Draw to an offscreen canvas or just use drawImage with ImageBitmap.
          // Since we already have processedData, we should convert to ImageBitmap or use a workaround.
          // Simple workaround: Create a temporary canvas/bitmap if needed, OR just draw processedDataRef to a temp canvas once and draw THAT.
          
          // Better: We should store processed result as an ImageBitmap or HTMLImageElement if possible?
          // For now, let's create a temp canvas to draw the imageData, then draw that canvas.
          // This is expensive every frame? NO, only on render.
          
          const tempCanvas = document.createElement('canvas');
          tempCanvas.width = canvas.width;
          tempCanvas.height = canvas.height;
          tempCanvas.getContext('2d')!.putImageData(processedDataRef.current, 0, 0);
          ctx.drawImage(tempCanvas, 0, 0);
          
      } else if (originalImageRef.current) {
          // If no processed data, just draw original
          ctx.drawImage(originalImageRef.current, 0, 0, canvas.width, canvas.height);
      }
      
      // 3. Image Watermark
      if (useWatermark && watermarkImg) {
          ctx.save();
          ctx.globalAlpha = opacity;
          // Apply watermark scale
          const w = watermarkImg.width * watermarkScale;
          const h = watermarkImg.height * watermarkScale;
          ctx.drawImage(watermarkImg, watermarkPos.x, watermarkPos.y, w, h);
          ctx.restore();
      }
      
      ctx.restore(); // Restore View Transform
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
      if (!canvas) return { visualX: 0, visualY: 0, worldX: 0, worldY: 0 };
      
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
      
      // Visual coordinates on the canvas DOM element
      const visualX = (clientX - rect.left) * scaleX;
      const visualY = (clientY - rect.top) * scaleY;
      
      // Map visual to World (considering viewTransform)
      // visual = world * scale + translate
      // world = (visual - translate) / scale
      
      return {
          visualX: visualX || 0, // Fallback for safety, though math should ideally be valid
          visualY: visualY || 0,
          worldX: ((visualX || 0) - viewTransform.x) / viewTransform.scale,
          worldY: ((visualY || 0) - viewTransform.y) / viewTransform.scale
      };
  };

  const handleMouseDown = (e: React.MouseEvent | React.TouchEvent) => {
      e.preventDefault();
      const { visualX, visualY, worldX, worldY } = getCanvasCoordinates(e);
      
      // Determine action (Drag Watermark vs Pan)
      // If we hit watermark -> Drag Watermark
      // Else -> Pan
      
      let hitWatermark = false;
      if (useWatermark && watermarkImg) {
          const w = watermarkImg.width * watermarkScale;
          const h = watermarkImg.height * watermarkScale;
          if (worldX >= watermarkPos.x && worldX <= watermarkPos.x + w &&
              worldY >= watermarkPos.y && worldY <= watermarkPos.y + h) {
              hitWatermark = true;
          }
      }
      
      // Right click force Pan?
      const isRightClick = 'button' in e && (e as React.MouseEvent).button === 2;
      
      if (hitWatermark && !isRightClick) {
          setIsDragging(true);
          dragOffset.current = { x: worldX - watermarkPos.x, y: worldY - watermarkPos.y };
      } else {
          setIsPanning(true);
          lastMousePos.current = { x: visualX, y: visualY };
      }
  };
  
  const handleMouseMove = (e: React.MouseEvent | React.TouchEvent) => {
      e.preventDefault(); 
      const { visualX, visualY, worldX, worldY } = getCanvasCoordinates(e);
      
      if (isDragging && watermarkImg) {
          setWatermarkPos({
              x: worldX - dragOffset.current.x,
              y: worldY - dragOffset.current.y
          });
          requestAnimationFrame(renderCanvas);
      } else if (isPanning) {
          const deltaX = visualX - lastMousePos.current.x;
          const deltaY = visualY - lastMousePos.current.y;
          
          setViewTransform(prev => ({
              ...prev,
              x: prev.x + deltaX,
              y: prev.y + deltaY
          }));
          
          lastMousePos.current = { x: visualX, y: visualY };
          requestAnimationFrame(renderCanvas);
      }
  };
  
  const handleMouseUp = () => {
      setIsDragging(false);
      setIsPanning(false);
  };
  
  const handleWheel = (e: React.WheelEvent) => {
      // Zoom
      e.preventDefault();
      e.stopPropagation();
      
      const { visualX, visualY } = getCanvasCoordinates(e);
      // We want the point under mouse (worldX before zoom) to stay under mouse (worldX after zoom)
      // worldX = (visualX - tx) / scale
      // visualX = worldX * scale + tx
      
      const zoomFactor = e.deltaY < 0 ? 1.1 : 0.9;
      let newScale = viewTransform.scale * zoomFactor;
      
      if (newScale < 0.1) newScale = 0.1;
      if (newScale > 10) newScale = 10;
      
      // Calculate new translate
      // visualX - newTx = (visualX - oldTx) / oldScale * newScale
      // newTx = visualX - (visualX - oldTx) * (newScale / oldScale)
      
      const newX = visualX - (visualX - viewTransform.x) * (newScale / viewTransform.scale);
      const newY = visualY - (visualY - viewTransform.y) * (newScale / viewTransform.scale);
      
      setViewTransform({ scale: newScale, x: newX, y: newY });
      requestAnimationFrame(renderCanvas);
  };
  
  // Disable context menu for right-click panning
  const handleContextMenu = (e: React.MouseEvent) => {
      e.preventDefault();
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
                Size
                <span>{Math.round(watermarkScale * 100)}%</span>
              </label>
              <input 
                type="range" 
                min="0.1" 
                max="3.0" 
                step="0.1" 
                value={watermarkScale} 
                onChange={(e) => {
                    setWatermarkScale(Number(e.target.value));
                    requestAnimationFrame(renderCanvas);
                }} 
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
          onWheel={handleWheel}
          onContextMenu={handleContextMenu}
          onTouchStart={handleMouseDown}
          onTouchMove={handleMouseMove}
          onTouchEnd={handleMouseUp}
        />
      </div>
    </div>
  );
}

export default App;
