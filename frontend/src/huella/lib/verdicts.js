/*
 * Veredictos de verificación. Se deciden SOLO con hashMatches y
 * registeredInBlockchain (B1); el texto de `message` nunca se interpreta.
 */

export const VERDICTS = {
  integro: {
    label: 'Íntegro', tone: 'seal', title: 'Documento íntegro',
    text: 'El archivo es idéntico al registrado y su huella está registrada en la blockchain activa.',
    next: 'Puedes aceptar el documento.',
    stamp: 'ÍNTEGRO', ring: 'Huella SHA-256 idéntica · Evidencia en blockchain · ',
  },
  alterado: {
    label: 'Alterado', tone: 'alert', title: 'Documento alterado',
    text: 'El archivo presentado difiere del registrado: al menos un byte fue modificado, y su huella no está en la blockchain activa.',
    next: 'No aceptes este archivo. Pide la versión original a quien lo emitió.',
    stamp: 'ALTERADO', ring: 'La huella no coincide · No aceptar este archivo · ',
  },
  distinto: {
    label: 'Otro registro', tone: 'info', title: 'Es otro documento registrado',
    text: 'La huella del archivo no coincide con el registro elegido, pero sí está registrada en la blockchain activa.',
    next: 'Revisa que hayas elegido el registro correcto.',
    stamp: 'OTRO REGISTRO', ring: 'Existe en blockchain · No es el seleccionado · ',
  },
  sin_evidencia: {
    label: 'Sin evidencia en cadena', tone: 'warn', title: 'Sin evidencia en cadena',
    text: 'La huella coincide con la registrada en PostgreSQL, pero no está en la blockchain activa.',
    next: 'El registro debe revisarse: la cadena pudo reiniciarse después de registrarlo.',
    stamp: 'SIN EVIDENCIA', ring: 'La huella no está en blockchain · Revisar el registro · ',
  },
  incompleta: {
    label: 'Verificación incompleta', tone: 'warn', title: 'Verificación incompleta',
    text: 'No se pudo consultar la blockchain activa, así que no hay un resultado definitivo.',
    next: 'Comprueba que el nodo de Hardhat esté en ejecución e inténtalo de nuevo.',
    stamp: 'INCOMPLETA', ring: 'Sin consulta a la blockchain · Sin resultado · ',
  },
};

/* null si el backend no informó registeredInBlockchain (respuesta anterior a B1 sin datos suficientes). */
export function verdictOf(v) {
  if (v.registeredInBlockchain === null || v.registeredInBlockchain === undefined) return 'incompleta';
  if (v.hashMatches) return v.registeredInBlockchain ? 'integro' : 'sin_evidencia';
  return v.registeredInBlockchain ? 'distinto' : 'alterado';
}
