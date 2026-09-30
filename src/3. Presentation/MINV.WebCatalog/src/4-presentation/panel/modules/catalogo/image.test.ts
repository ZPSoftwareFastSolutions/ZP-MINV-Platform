// Módulo «Catálogo» · pruebas de la imagen del producto en el navegador: base64 del contrato, una foto liviana se envía tal
// cual, una grande se reduce a JPEG bajando la calidad hasta 1 MB, y los archivos que no sirven se explican en palabras.
// jsdom no dibuja: el lienzo se reemplaza por herramientas de prueba.

import { describe, expect, it, vi } from 'vitest';
import { ImageProblem, MAX_IMAGE_BYTES, bytesToBase64, imageDataUrl, prepareImage, type DecodedImage, type ImageTools } from './image';

function decoded(width: number, height: number): DecodedImage {
  return { width, height, source: {} as CanvasImageSource, close: vi.fn() };
}

function tools(width: number, height: number, sizes: number[] = []) {
  const encodeJpeg = vi.fn<ImageTools['encodeJpeg']>(async () => new Blob([new Uint8Array(sizes.shift() ?? 10)], { type: 'image/jpeg' }));
  const decode = vi.fn<ImageTools['decode']>(async () => decoded(width, height));
  return { decode, encodeJpeg };
}

describe('Catálogo · imagen del producto', () => {
  it('bytes → base64 (el `byte[]` del contrato) y la vista previa `data:`', () => {
    expect(bytesToBase64(new Uint8Array([77, 45, 73, 78, 86]))).toBe('TS1JTlY=');
    expect(imageDataUrl('image/png', 'AAA=')).toBe('data:image/png;base64,AAA=');
  });

  it('una foto PNG o JPEG liviana (≤ 1 MB y ≤ 1600 px) se envía tal cual', async () => {
    const file = new File([new Uint8Array([1, 2, 3])], 'monitor.png', { type: 'image/png' });
    const helper = tools(800, 600);
    const prepared = await prepareImage(file, helper);
    expect(prepared).toMatchObject({ content: 'AQID', contentType: 'image/png', fileName: 'monitor.png', bytes: 3, dataUrl: 'data:image/png;base64,AQID' });
    expect(helper.encodeJpeg).not.toHaveBeenCalled();
  });

  it('una foto grande se reduce a 800 px y se guarda como JPEG, bajando la calidad hasta que entra en 1 MB', async () => {
    const file = new File([new Uint8Array(10)], 'foto.webp', { type: 'image/webp' });
    const helper = tools(4000, 2000, [MAX_IMAGE_BYTES + 5, 900]);
    const prepared = await prepareImage(file, helper);
    expect(helper.encodeJpeg).toHaveBeenCalledTimes(2);
    expect(helper.encodeJpeg.mock.calls.map((call) => call.slice(1))).toEqual([
      [800, 400, 0.88],
      [800, 400, 0.75],
    ]);
    expect(prepared).toMatchObject({ contentType: 'image/jpeg', fileName: 'foto.jpg', bytes: 900 });
  });

  it('explica lo que no sirve: otro tipo de archivo, una imagen dañada o demasiado grande incluso reducida', async () => {
    await expect(prepareImage(new File(['hola'], 'nota.txt', { type: 'text/plain' }), tools(1, 1))).rejects.toThrow('no es una imagen');
    const broken: ImageTools = { decode: async () => Promise.reject(new Error('dañada')), encodeJpeg: async () => null };
    await expect(prepareImage(new File([new Uint8Array(1)], 'x.png', { type: 'image/png' }), broken)).rejects.toThrow('no es una imagen válida');
    const huge = tools(5000, 5000, [MAX_IMAGE_BYTES + 1, MAX_IMAGE_BYTES + 1, MAX_IMAGE_BYTES + 1, MAX_IMAGE_BYTES + 1]);
    await expect(prepareImage(new File([new Uint8Array(MAX_IMAGE_BYTES + 1)], 'gigante.jpg', { type: 'image/jpeg' }), huge)).rejects.toBeInstanceOf(ImageProblem);
    const noCanvas: ImageTools = { decode: async () => decoded(3000, 3000), encodeJpeg: async () => null };
    await expect(prepareImage(new File([new Uint8Array(1)], 'grande.png', { type: 'image/png' }), noCanvas)).rejects.toThrow('no puede reducir');
  });
});
