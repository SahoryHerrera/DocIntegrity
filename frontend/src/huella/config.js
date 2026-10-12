/*
 * Configuración de la interfaz. La URL de la API y los datos de la red se
 * leerán de variables de entorno (VITE_*) en la etapa 2.
 */
export const APP = {
  name: 'Huella',
  maxFileMB: 20,
};

/* Endpoints implementados en DocIntegrity.Api (DocumentsController y AuthController). */
export const ENDPOINTS = [
  ['POST', '/api/auth/google', 'Valida el ID token de Google y emite el JWT del sistema.'],
  ['GET', '/api/auth/me', 'Devuelve el usuario del JWT. Requiere Authorization.'],
  ['POST', '/api/documents/register', 'Calcula la huella, analiza con IA, registra en blockchain y guarda el documento.'],
  ['GET', '/api/documents', 'Lista los documentos registrados, del más reciente al más antiguo.'],
  ['GET', '/api/documents/{id}', 'Devuelve el detalle de un documento registrado.'],
  ['POST', '/api/documents/{id}/verify', 'Verifica la integridad de un archivo frente al registro.'],
  ['POST', '/api/documents/analyze', 'Analiza un PDF con IA sin guardarlo.'],
  ['POST', '/api/documents/extract-text', 'Extrae el texto de un PDF sin guardarlo.'],
];

/* Funciones públicas de DocumentRegistry.sol. */
export const CONTRACT_FUNCTIONS = [
  ['registerDocument(bytes32 documentHash)', 'Transacción. Guarda la huella; revierte si ya estaba registrada.'],
  ['isDocumentRegistered(bytes32 documentHash) → bool', 'Consulta de solo lectura (view). No consume gas. Se usa al verificar.'],
  ['getDocument(bytes32 documentHash) → (bytes32, uint256, address, bool)', 'Consulta de solo lectura con la fecha y la cuenta que registró la huella.'],
];
