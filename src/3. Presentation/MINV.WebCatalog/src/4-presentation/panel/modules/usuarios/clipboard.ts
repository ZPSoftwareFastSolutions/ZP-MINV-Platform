// Módulo «Usuarios» · copiar un texto al portapapeles (la contraseña temporal que se muestra una sola vez). Sin red ni
// almacenamiento: usa el portapapeles del navegador si lo permite (página segura y permiso); si no, quien llama deja el
// texto seleccionado para copiarlo a mano.

/** Copia el texto; false si el navegador no lo permite. */
export async function copyText(text: string): Promise<boolean> {
  try {
    const clipboard = typeof navigator === 'undefined' ? undefined : navigator.clipboard;
    if (!clipboard || typeof clipboard.writeText !== 'function') return false;
    await clipboard.writeText(text);
    return true;
  } catch {
    return false;
  }
}
