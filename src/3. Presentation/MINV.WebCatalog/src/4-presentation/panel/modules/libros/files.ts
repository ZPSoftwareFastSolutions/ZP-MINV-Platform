// Módulo «Libros fiscales» · descarga del libro que arma el servidor (`ExportFiscalBookQuery`: CSV con el orden de las
// plantillas del SIN o Excel): llega en base64 dentro de la respuesta del RPC y se descarga con un enlace temporal
// (`blob:`), como la exportación a CSV del panel. Sin red ni almacenamiento del navegador.

import type { RpcResponseOf } from '@/4-presentation/app/contract';

/** Archivo tal como lo manda el servidor (`FiscalFile`: nombre, tipo y contenido en base64). */
export type ServerFileData = RpcResponseOf<'ExportFiscalBookQuery'>;

/** Bytes de un texto en base64 (null si el texto no es base64 válido). */
export function base64ToBytes(base64: string): Uint8Array<ArrayBuffer> | null {
  try {
    const binary = atob(base64.replace(/\s+/g, ''));
    const bytes = new Uint8Array(binary.length);
    for (let index = 0; index < binary.length; index += 1) bytes[index] = binary.charCodeAt(index);
    return bytes;
  } catch {
    return null;
  }
}

/** Nombre de archivo seguro («../factura 12.pdf» → «factura-12.pdf»); si no hay, el de respaldo. */
export function safeFileName(name: string, fallback: string): string {
  const base = name.split(/[/\\]/).pop() ?? '';
  const clean = base
    .normalize('NFD')
    .replace(/\p{Diacritic}/gu, '')
    .replace(/[^A-Za-z0-9._-]+/g, '-')
    .replace(/^[-.]+|-+$/g, '');
  return clean.length > 0 ? clean.slice(0, 120) : fallback;
}

/** Descarga el archivo. Devuelve el nombre con que se descargó, o null si el contenido no se pudo leer. */
export function downloadServerFile(file: ServerFileData, fallbackName: string): string | null {
  const bytes = base64ToBytes(file.content);
  if (!bytes) return null;
  const fileName = safeFileName(file.fileName, fallbackName);
  const blob = new Blob([bytes], { type: file.contentType || 'application/octet-stream' });
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
