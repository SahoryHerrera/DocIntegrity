import { APP } from '../config.js';
import { fmtBytes } from './format.js';

export const isPdf = (bytes) => bytes.length > 4 && bytes[0] === 0x25 && bytes[1] === 0x50 && bytes[2] === 0x44 && bytes[3] === 0x46;

/* Validación previa en el navegador. El backend vuelve a validar el archivo. */
export async function validatePdf(file) {
  if (!file) return { error: 'No se recibió ningún archivo.' };
  if (file.size > APP.maxFileMB * 1024 * 1024) return { error: `«${file.name}» pesa ${fmtBytes(file.size)}. El máximo es ${APP.maxFileMB} MB.` };
  const bytes = new Uint8Array(await file.arrayBuffer());
  if (!bytes.length) return { error: `«${file.name}» está vacío.` };
  if (!isPdf(bytes)) return { error: `«${file.name}» no es un PDF. Elige un documento en formato PDF.` };
  return { bytes };
}
