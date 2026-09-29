// Puerto del ALMACÉN del carrito (V7, regla P-08): dónde queda guardado el carrito entre visitas. Lo implementa la
// infraestructura (`3-infrastructure/storage`: el almacenamiento del navegador, o la memoria cuando el navegador no lo
// permite y en las pruebas). Guarda SOLO el SKU y la cantidad de cada línea: nunca precios ni datos de la persona.
//
// Es síncrono y NUNCA lanza: si el almacenamiento está lleno, bloqueado o trae datos que no sirven, el carrito sigue
// funcionando en memoria y la página no se rompe.

import type { CartItem } from '@/1-domain/cart/types';

export interface ICartStore {
  /** Verdadero si lo guardado sobrevive al cerrar la pestaña (falso: solo vive en memoria). */
  readonly persistent: boolean;
  /** Líneas guardadas, ya validadas. Vacío si no hay nada o si lo guardado no sirve (dato roto o de otro formato). */
  load(): CartItem[];
  /** Guarda las líneas (vacío = borra lo guardado). Devuelve false si no se pudo guardar. */
  save(items: readonly CartItem[]): boolean;
  /** Avisa cuando OTRA pestaña cambió el carrito, con las líneas nuevas. Devuelve cómo dejar de escuchar. */
  subscribe(listener: (items: CartItem[]) => void): () => void;
}
