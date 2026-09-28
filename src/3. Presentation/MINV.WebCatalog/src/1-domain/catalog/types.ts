// Dominio del catálogo web (V5). Sin dependencias de React ni de la infraestructura: solo tipos y reglas puras.

export type CategoryCode = string;

export type Condition = 'Nuevo' | 'Reacondicionado' | 'Usado';

export type ProductTag = 'destacado' | 'oferta' | 'nuevo';

export interface Category {
  code: CategoryCode;
  name: string;
  slug: string;
  /** Código de la categoría madre; null en las raíces (Componentes, Computadoras, Monitores…). */
  parent: CategoryCode | null;
  /** Nombre del ícono de lucide-react. */
  icon: string;
  description: string;
  productCount: number;
}

export interface Brand {
  code: string;
  name: string;
  productCount: number;
}

export type SpecValue = string | number | string[];

export interface Spec {
  key: string;
  label: string;
  value: SpecValue;
  /** Valor ya formateado para mostrar («5,1 GHz», «DDR5», «Sí»). */
  text: string;
  unit: string | null;
  /** Solo las filtrables alimentan los filtros y facetas del catálogo. */
  filterable: boolean;
}

export interface Product {
  sku: string;
  slug: string;
  name: string;
  shortName: string;
  category: CategoryCode;
  categoryName: string;
  categoryPath: string;
  brand: string;
  /** Precio de venta en bolivianos (Bs), IVA incluido. */
  price: number;
  /** Precio de lista tachado cuando el producto está en oferta (solo presentación). */
  listPrice: number | null;
  /** URL de la imagen (absoluta hacia la API en la V6; vacía si el producto no tiene imagen). */
  image: string;
  /** Disponible = existencias − reservado en la sucursal de la tienda (nunca negativo). */
  stock: number;
  /** Unidades reservadas (reservas web y del escritorio): explica por qué lo disponible baja sin ventas. */
  reserved: number;
  condition: Condition;
  warrantyMonths: number;
  serialized: boolean;
  popularity: number;
  tags: ProductTag[];
  description: string;
  highlights: string[];
  specs: Spec[];
}
