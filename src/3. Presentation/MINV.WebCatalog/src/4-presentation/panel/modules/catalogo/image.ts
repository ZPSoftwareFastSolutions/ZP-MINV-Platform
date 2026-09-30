// Módulo «Catálogo» · la imagen del producto en el navegador, sin red ni almacenamiento: se lee el archivo elegido, se
// reduce con un lienzo (canvas) si hace falta y se convierte al formato del contrato (`byte[]` viaja en base64). Las mismas
// reglas del escritorio (`ImageFiles.Pick`) y del servidor (`ProductImage`): PNG o JPEG de hasta 1 MB. Una foto PNG o
// JPEG liviana (≤ 1 MB y ≤ 1600 px) se envía tal cual; cualquier otra se reduce a 800 px y se guarda como JPEG, bajando la
// calidad hasta que entre en 1 MB.

export const MAX_IMAGE_BYTES = 1024 * 1024;
const KEEP_MAX_SIDE = 1600;
const RESIZE_MAX_SIDE = 800;
const QUALITIES: readonly number[] = [0.88, 0.75, 0.6, 0.45];

/** La imagen lista para `SetProductImageCommand`. */
export interface PreparedImage {
  /** Contenido en base64 (sin el prefijo «data:»). */
  content: string;
  contentType: 'image/png' | 'image/jpeg';
  fileName: string;
  /** Para la vista previa. */
  dataUrl: string;
  bytes: number;
}

/** Una imagen ya decodificada (para medirla y dibujarla). */
export interface DecodedImage {
  width: number;
  height: number;
  source: CanvasImageSource;
  close(): void;
}

/** Lo que usa `prepareImage` del navegador (las pruebas lo reemplazan: jsdom no dibuja). */
export interface ImageTools {
  decode(file: Blob): Promise<DecodedImage>;
  /** Dibuja la imagen en ese tamaño y la guarda como JPEG con esa calidad (null si el navegador no puede). */
  encodeJpeg(image: DecodedImage, width: number, height: number, quality: number): Promise<Blob | null>;
}

export class ImageProblem extends Error {}

/** Bytes → base64 (en tramos, para no pasar el límite de argumentos con archivos de 1 MB). */
export function bytesToBase64(bytes: Uint8Array): string {
  let binary = '';
  const chunk = 0x8000;
  for (let index = 0; index < bytes.length; index += chunk) binary += String.fromCharCode(...bytes.subarray(index, index + chunk));
  return btoa(binary);
}

export async function blobBytes(blob: Blob): Promise<Uint8Array> {
  if (typeof blob.arrayBuffer === 'function') return new Uint8Array(await blob.arrayBuffer());
  return new Promise((resolve, reject) => {
    const reader = new FileReader();
    reader.onload = () => resolve(new Uint8Array(reader.result as ArrayBuffer));
    reader.onerror = () => reject(reader.error ?? new Error('No se pudo leer el archivo.'));
    reader.readAsArrayBuffer(blob);
  });
}

/** `data:` para mostrar una imagen del servidor o la elegida (la política de contenido admite `data:` en imágenes). */
export function imageDataUrl(contentType: string, base64: string): string {
  return `data:${contentType};base64,${base64}`;
}

function extensionOf(name: string): string {
  const dot = name.lastIndexOf('.');
  return dot >= 0 ? name.slice(dot).toLowerCase() : '';
}

function asJpegName(name: string): string {
  const dot = name.lastIndexOf('.');
  return `${dot > 0 ? name.slice(0, dot) : name || 'imagen'}.jpg`;
}

async function toPrepared(blob: Blob, contentType: PreparedImage['contentType'], fileName: string): Promise<PreparedImage> {
  const bytes = await blobBytes(blob);
  const content = bytesToBase64(bytes);
  return { content, contentType, fileName, dataUrl: imageDataUrl(contentType, content), bytes: bytes.length };
}

/** Prepara el archivo elegido. Lanza `ImageProblem` con el motivo en palabras. */
export async function prepareImage(file: File, tools: ImageTools = BROWSER_TOOLS): Promise<PreparedImage> {
  if (file.type && !file.type.startsWith('image/')) throw new ImageProblem('El archivo elegido no es una imagen. Elija una foto PNG o JPEG.');
  let image: DecodedImage;
  try {
    image = await tools.decode(file);
  } catch {
    throw new ImageProblem('El archivo elegido no es una imagen válida.');
  }
  try {
    const extension = extensionOf(file.name);
    const type = file.type === 'image/png' || extension === '.png' ? 'image/png' : file.type === 'image/jpeg' || extension === '.jpg' || extension === '.jpeg' ? 'image/jpeg' : null;
    if (type && file.size <= MAX_IMAGE_BYTES && Math.max(image.width, image.height) <= KEEP_MAX_SIDE) return await toPrepared(file, type, file.name);
    const scale = Math.min(1, RESIZE_MAX_SIDE / Math.max(image.width, image.height, 1));
    const width = Math.max(1, Math.round(image.width * scale));
    const height = Math.max(1, Math.round(image.height * scale));
    for (const quality of QUALITIES) {
      const jpeg = await tools.encodeJpeg(image, width, height, quality);
      if (!jpeg) throw new ImageProblem('Este navegador no puede reducir la imagen: elija una foto PNG o JPEG de menos de 1 MB.');
      if (jpeg.size <= MAX_IMAGE_BYTES) return await toPrepared(jpeg, 'image/jpeg', asJpegName(file.name));
    }
    throw new ImageProblem('La imagen es demasiado grande incluso reducida: elija una de menos de 1 MB.');
  } finally {
    image.close();
  }
}

/** Decodifica con `createImageBitmap` (o con una etiqueta <img> si no existe) y dibuja en un lienzo. */
export const BROWSER_TOOLS: ImageTools = {
  async decode(file) {
    if (typeof createImageBitmap === 'function') {
      const bitmap = await createImageBitmap(file);
      return { width: bitmap.width, height: bitmap.height, source: bitmap, close: () => bitmap.close() };
    }
    const url = URL.createObjectURL(file);
    try {
      const element = new Image();
      await new Promise<void>((resolve, reject) => {
        element.onload = () => resolve();
        element.onerror = () => reject(new Error('imagen'));
        element.src = url;
      });
      return { width: element.naturalWidth, height: element.naturalHeight, source: element, close: () => undefined };
    } finally {
      URL.revokeObjectURL(url);
    }
  },
  async encodeJpeg(image, width, height, quality) {
    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    const context = canvas.getContext('2d');
    if (!context) return null;
    // Fondo blanco: un PNG con transparencia no queda negro al pasarlo a JPEG.
    context.fillStyle = '#fff';
    context.fillRect(0, 0, width, height);
    context.drawImage(image.source, 0, 0, width, height);
    return new Promise<Blob | null>((resolve) => canvas.toBlob((blob) => resolve(blob), 'image/jpeg', quality));
  },
};
