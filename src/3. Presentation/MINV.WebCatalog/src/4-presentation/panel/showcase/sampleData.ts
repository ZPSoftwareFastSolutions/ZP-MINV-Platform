// Datos de EJEMPLO de la página interna de componentes (`/panel/_componentes`, solo en desarrollo). Son inventados y se
// generan igual en cada carga (azar con semilla): ninguna persona, venta ni NIT es real.

import type { ComboOption } from '../kit/ComboBox';
import { defineStatuses } from '../kit/statuses';

export const SALE_STATUSES = defineStatuses({
  Paid: { label: 'Pagada', tone: 'success' },
  Pending: { label: 'Pendiente', tone: 'warning' },
  Voided: { label: 'Anulada', tone: 'danger' },
});

export type SaleStatus = keyof typeof SALE_STATUSES;

export const BRANCHES: readonly { value: string; label: string }[] = [
  { value: 'CM', label: 'Casa matriz La Paz' },
  { value: 'CB', label: 'Sucursal Cochabamba' },
  { value: 'SC', label: 'Sucursal Santa Cruz' },
];

export function branchName(code: string): string {
  return BRANCHES.find((branch) => branch.value === code)?.label ?? code;
}

export interface SampleCustomer {
  id: string;
  name: string;
  nit: string | null;
}

export interface SampleLine {
  sku: string;
  name: string;
  quantity: number;
  price: number;
}

export interface SampleSale {
  number: string;
  /** Fecha y hora ISO. */
  date: string;
  branch: string;
  customer: SampleCustomer;
  status: SaleStatus;
  channel: 'Caja' | 'Web';
  lines: SampleLine[];
  items: number;
  total: number;
  notes: string | null;
}

export interface SampleProduct {
  sku: string;
  name: string;
  price: number;
  stock: number;
}

const FIRST = ['Lucía', 'Carlos', 'Ana', 'Jorge', 'Sofía', 'Diego', 'Valeria', 'Mateo', 'Camila', 'Andrés', 'Paola', 'Rodrigo', 'Daniela', 'Gabriel'];
const LAST = ['Mamani', 'Quispe', 'Rojas', 'Flores', 'Gutiérrez', 'Vargas', 'Choque', 'Condori', 'Torrez', 'Salazar', 'Arce', 'Ibáñez'];

export const PRODUCTS: readonly SampleProduct[] = [
  ['MON-27-QHD', 'Monitor 27" QHD 165 Hz', 2890, 12],
  ['MON-24-FHD', 'Monitor 24" Full HD 144 Hz', 1490, 20],
  ['SSD-NVME-1T', 'SSD NVMe 1 TB', 620, 35],
  ['SSD-NVME-2T', 'SSD NVMe 2 TB', 1150, 14],
  ['RAM-DDR5-32', 'Memoria DDR5 32 GB (2 × 16)', 980, 22],
  ['RAM-DDR4-16', 'Memoria DDR4 16 GB (2 × 8)', 420, 40],
  ['GPU-RTX-8G', 'Tarjeta de video 8 GB', 3490, 6],
  ['GPU-RTX-12G', 'Tarjeta de video 12 GB', 5290, 4],
  ['CPU-6C-AM5', 'Procesador 6 núcleos AM5', 1690, 9],
  ['CPU-8C-AM5', 'Procesador 8 núcleos AM5', 2690, 7],
  ['MB-B650', 'Placa madre B650 ATX', 1390, 11],
  ['PSU-750-G', 'Fuente 750 W 80 Plus Gold', 890, 16],
  ['CASE-MID-RGB', 'Gabinete ATX con vidrio y RGB', 690, 18],
  ['KB-MEC-TKL', 'Teclado mecánico TKL', 450, 30],
  ['MOUSE-GAM-26K', 'Mouse gamer 26 000 DPI', 320, 44],
  ['HS-71-WL', 'Audífonos inalámbricos 7.1', 590, 25],
  ['PAD-XL', 'Mousepad XL', 120, 60],
  ['CAM-1080', 'Cámara web 1080p', 380, 19],
  ['ROUTER-AX', 'Router Wi-Fi 6 AX3000', 760, 10],
  ['CTRL-PS5', 'Control inalámbrico para PS5', 690, 15],
  ['JUE-PS5-ACC', 'Juego de acción para PS5', 490, 28],
  ['CONS-PS5-SLIM', 'Consola PS5 Slim', 4990, 3],
  ['CONS-SWITCH2', 'Consola Nintendo Switch 2', 4390, 2],
  ['COOL-240-AIO', 'Refrigeración líquida 240 mm', 790, 8],
].map(([sku, name, price, stock]) => ({ sku: String(sku), name: String(name), price: Number(price), stock: Number(stock) }));

/** Azar con semilla (mulberry32): los mismos datos en cada carga. */
function random(seed: number): () => number {
  let state = seed >>> 0;
  return () => {
    state = (state + 0x6d2b79f5) >>> 0;
    let value = state;
    value = Math.imul(value ^ (value >>> 15), value | 1);
    value ^= value + Math.imul(value ^ (value >>> 7), value | 61);
    return ((value ^ (value >>> 14)) >>> 0) / 4294967296;
  };
}

function pick<T>(list: readonly T[], next: () => number): T {
  return list[Math.floor(next() * list.length)];
}

function roundMoney(value: number): number {
  return Math.round(value * 100) / 100;
}

function buildCustomers(): SampleCustomer[] {
  const next = random(7);
  return Array.from({ length: 28 }, (_, index) => ({
    id: `CLI-${String(index + 1).padStart(4, '0')}`,
    name: `${pick(FIRST, next)} ${pick(LAST, next)}`,
    nit: next() < 0.6 ? String(1_000_000 + Math.floor(next() * 8_999_999)) : null,
  }));
}

export const CUSTOMERS: readonly SampleCustomer[] = buildCustomers();

// La descripción lleva el código: dos clientes pueden llamarse igual y la persona tiene que poder distinguirlos.
export const CUSTOMER_OPTIONS: readonly ComboOption<SampleCustomer>[] = CUSTOMERS.map((customer) => ({
  value: customer.id,
  label: customer.name,
  description: `${customer.id} · ${customer.nit ? `NIT ${customer.nit}` : 'sin NIT'}`,
  data: customer,
}));

/** 137 ventas de ejemplo repartidas en los últimos ~50 días (desde `now`). */
export function buildSales(now: Date = new Date()): SampleSale[] {
  const next = random(2026);
  const counters: Record<string, number> = { CM: 0, CB: 0, SC: 0 };
  return Array.from({ length: 137 }, (_, index) => {
    const branch = pick(BRANCHES, next).value;
    counters[branch] += 1;
    const lines = Array.from({ length: 1 + Math.floor(next() * 3) }, () => {
      const product = pick(PRODUCTS, next);
      return { sku: product.sku, name: product.name, quantity: 1 + Math.floor(next() * 2), price: product.price };
    });
    const roll = next();
    const status: SaleStatus = roll < 0.78 ? 'Paid' : roll < 0.9 ? 'Pending' : 'Voided';
    return {
      number: `F-${branch}-${String(counters[branch] + 120).padStart(6, '0')}`,
      date: new Date(now.getTime() - index * 9 * 3_600_000 - Math.floor(next() * 3_600_000)).toISOString(),
      branch,
      customer: pick(CUSTOMERS, next),
      status,
      channel: next() < 0.8 ? 'Caja' : 'Web',
      lines,
      items: lines.reduce((sum, line) => sum + line.quantity, 0),
      total: roundMoney(lines.reduce((sum, line) => sum + line.quantity * line.price, 0)),
      // Una nota que empieza con «=» para comprobar que el CSV la neutraliza.
      notes: index === 3 ? '=SUMA(1;2) nota de prueba del CSV' : index % 11 === 0 ? 'Retira en tienda el sábado' : null,
    };
  });
}

/** Búsqueda simulada de productos (como lo haría el servidor): espera un poco y respeta la cancelación. */
export function searchProducts(query: string, signal: AbortSignal): Promise<ComboOption<SampleProduct>[]> {
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      const words = query.toLowerCase().split(/\s+/).filter(Boolean);
      const found = PRODUCTS.filter((product) => words.every((word) => `${product.sku} ${product.name}`.toLowerCase().includes(word)));
      resolve(found.map((product) => ({ value: product.sku, label: product.name, description: `${product.sku} · stock ${product.stock}`, data: product })));
    }, 350);
    signal.addEventListener('abort', () => {
      clearTimeout(timer);
      reject(new DOMException('Búsqueda cancelada', 'AbortError'));
    });
  });
}
