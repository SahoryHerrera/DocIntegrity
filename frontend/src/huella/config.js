/*
 * Configuración de la interfaz. Los valores de entorno (VITE_*) se incrustan en
 * el código del navegador al compilar: nunca deben contener secretos.
 */
const env = import.meta.env ?? {};

export const APP = {
  name: 'Huella',
  maxFileMB: 20,
};

export const API = {
  baseUrl: String(env.VITE_API_URL || '').trim().replace(/\/+$/, ''),
  // Tiempo de espera general y el de las operaciones que pasan por Ollama
  // (el HttpClient del backend espera hasta 5 minutos al modelo).
  timeoutMs: 30_000,
  longTimeoutMs: 360_000,
};

/* Solo para mostrar; el backend tiene su propia configuración de blockchain. */
export const CHAIN = {
  contractAddress: String(env.VITE_CONTRACT_ADDRESS || '').trim(),
  network: String(env.VITE_CHAIN_NAME || 'Hardhat local').trim(),
};

/* Endpoints implementados en DocIntegrity.Api (DocumentsController y AuthController). */
export const ENDPOINTS = [
  ['POST', '/api/auth/google', 'Público. Valida el ID token de Google y fija la cookie de sesión.'],
  ['GET', '/api/auth/me', 'Usuario y vencimiento de la sesión actual.'],
  ['POST', '/api/auth/logout', 'Público. Borra la cookie de sesión.'],
  ['POST', '/api/documents/register', 'Registra un PDF en tu biblioteca; reutiliza la evidencia si la huella ya está en blockchain.'],
  ['GET', '/api/documents', 'Lista solo los documentos de tu biblioteca.'],
  ['GET', '/api/documents/{id}', 'Detalle de un documento propio (404 si es ajeno).'],
  ['GET', '/api/documents/{id}/blockchain-status', 'Estado en la blockchain activa de un documento propio.'],
  ['POST', '/api/documents/{id}/verify', 'Verifica un archivo frente a un documento propio.'],
  ['POST', '/api/documents/analyze', 'Analiza un PDF con IA sin guardarlo.'],
  ['POST', '/api/documents/extract-text', 'Extrae el texto de un PDF sin guardarlo.'],
];

/* Funciones públicas de DocumentRegistry.sol. */
export const CONTRACT_FUNCTIONS = [
  ['registerDocument(bytes32 documentHash)', 'Transacción. Guarda la huella; revierte si ya estaba registrada.'],
  ['isDocumentRegistered(bytes32 documentHash) → bool', 'Consulta de solo lectura (view). No consume gas. Se usa al verificar.'],
  ['getDocument(bytes32 documentHash) → (bytes32, uint256, address, bool)', 'Consulta de solo lectura con la fecha y la cuenta que registró la huella.'],
];
