// Dominio del carrito de compras (V7). El carrito es una lista de líneas por SKU con su cantidad: NO guarda precios,
// nombres ni disponibilidad (eso sale siempre del catálogo fresco, al cruzarlo en 2-application/cart). Sin React, sin
// red y sin almacenamiento: solo tipos y reglas puras.

/** Una línea del carrito: lo ÚNICO que se guarda en el navegador. */
export interface CartItem {
  /** SKU del producto tal como lo publica el catálogo (mayúsculas, letras, números, guion y guion bajo). */
  sku: string;
  /** Unidades pedidas: entero de 1 a 16. */
  quantity: number;
}

/** El carrito: líneas en el orden en que se agregaron, una por SKU. */
export interface Cart {
  readonly items: readonly CartItem[];
}

/** Por qué no se agregó todo lo pedido (o nada). */
export type CartAddLimit =
  /** El producto no tiene unidades disponibles. */
  | 'unavailable'
  /** El carrito ya tiene el máximo de productos distintos. */
  | 'cart_full'
  /** Se llegó a lo disponible del producto. */
  | 'stock'
  /** Se llegó al máximo de unidades por producto. */
  | 'line_limit'
  /** El producto no tiene un SKU válido. */
  | 'invalid';

/** Resultado de agregar al carrito. */
export interface CartAddResult {
  cart: Cart;
  /** Unidades que entraron de verdad (0 si no entró ninguna). */
  added: number;
  /** Unidades de ese producto que quedaron en el carrito. */
  quantity: number;
  /** null cuando entró todo lo pedido. */
  limit: CartAddLimit | null;
}
