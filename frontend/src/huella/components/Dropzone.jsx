import { useRef, useState } from 'react';
import { APP } from '../config.js';
import { Icon } from './Icon.jsx';

export default function Dropzone({ id, title, hint, onFile }) {
  const input = useRef(null);
  const [over, setOver] = useState(false);
  const pick = () => input.current && input.current.click();
  const take = (file) => { if (file) onFile(file); };
  return (
    <div
      className={`dropzone ${over ? 'is-over' : ''}`}
      role="button" tabIndex={0} aria-describedby={`${id}-hint`}
      onClick={(e) => { if (e.target !== input.current) pick(); }}
      onKeyDown={(e) => { if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); pick(); } }}
      onDragEnter={(e) => { e.preventDefault(); setOver(true); }}
      onDragOver={(e) => { e.preventDefault(); setOver(true); }}
      onDragLeave={() => setOver(false)}
      onDragEnd={() => setOver(false)}
      onDrop={(e) => { e.preventDefault(); setOver(false); take(e.dataTransfer.files[0]); }}
    >
      <input
        ref={input} type="file" id={`${id}-file`} className="sr-only" accept="application/pdf,.pdf" tabIndex={-1}
        onChange={(e) => { take(e.target.files[0]); e.target.value = ''; }}
      />
      <span className="dz-icon"><Icon name="upload" /></span>
      <span className="dz-title">{title}</span>
      <span className="dz-hint" id={`${id}-hint`}>{hint || `o haz clic para elegirlo. PDF de hasta ${APP.maxFileMB} MB.`}</span>
    </div>
  );
}
