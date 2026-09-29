// Módulo «Caja» · descarga de un archivo que devolvió el servidor (la factura en PDF de `RenderFiscalDocumentQuery`: el
// contenido viaja en base64, como todo `byte[]` del contrato). Sin red ni almacenamiento: el archivo se arma en la página
// y se descarga con un enlace temporal (`blob:`), igual que la exportación a CSV del panel.

import type { FiscalFileData } from './types';

/** «factura-45.pdf»: sin rutas ni caracteres raros. */
export function safeFileName(name: string, fallback = 'factura.pdf'): string {
  const clean = name
    .split(/[\\/]/)
    .pop()
    ?.replace(/[^\p{L}\p{N}._ -]+/gu, '')
    .trim();
  return clean && clean.length > 0 ? clean : fallback;
}

/** Bytes de un texto en base64 (lanza si no es base64). */
export function base64ToBytes(content: string): Uint8Array<ArrayBuffer> {
  const binary = atob(content.replace(/\s+/g, ''));
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index += 1) bytes[index] = binary.charCodeAt(index);
  return bytes;
}

/** Descarga el archivo en el navegador y devuelve su nombre (para el aviso «Se descargó …»). */
export function downloadServerFile(file: FiscalFileData): string {
  const fileName = safeFileName(file.fileName);
  const blob = new Blob([base64ToBytes(file.content)], { type: file.contentType || 'application/octet-stream' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = fileName;
  link.rel = 'noopener';
  link.hidden = true;
  document.body.appendChild(link);
  try {
    link.click();
  } finally {
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
  }
  return fileName;
}
