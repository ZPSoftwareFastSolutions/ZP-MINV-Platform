// Ranuras del armador «Armá tu PC»: qué categorías admite cada paso, cuáles son obligatorias y su consejo.

import type { CategoryCode, Product } from '@/1-domain/catalog/types';
import type { BuildSlot, SlotKey } from './types';

export const BUILD_SLOTS: readonly BuildSlot[] = [
  {
    key: 'cpu',
    order: 1,
    label: 'Procesador',
    categories: ['CPU'],
    required: true,
    multiple: false,
    hint: 'El procesador define la potencia de tu PC. Elegilo primero: de él dependen la placa madre y la memoria.',
    icon: 'Cpu',
  },
  {
    key: 'motherboard',
    order: 2,
    label: 'Placa madre',
    categories: ['MB'],
    required: true,
    multiple: false,
    hint: 'Conecta todos los componentes. Fijate que el socket coincida con tu procesador y el tipo de memoria con tu RAM.',
    icon: 'CircuitBoard',
  },
  {
    key: 'ram',
    order: 3,
    label: 'Memoria RAM',
    categories: ['RAM'],
    required: true,
    multiple: false,
    hint: '16 GB alcanzan para jugar; 32 GB si además editás video o transmitís.',
    icon: 'MemoryStick',
  },
  {
    key: 'gpu',
    order: 4,
    label: 'Tarjeta de video',
    categories: ['GPU'],
    required: false,
    multiple: false,
    hint: 'La pieza más importante para jugar. Elegila según la resolución y los Hz de tu monitor.',
    icon: 'Gpu',
  },
  {
    key: 'storage',
    order: 5,
    label: 'Almacenamiento',
    categories: ['STO'],
    required: true,
    multiple: true,
    hint: 'Un SSD NVMe para el sistema y los juegos; podés sumar más unidades para tus archivos.',
    icon: 'HardDrive',
  },
  {
    key: 'psu',
    order: 6,
    label: 'Fuente de poder',
    categories: ['PSU'],
    required: true,
    multiple: false,
    hint: 'Elegí una fuente certificada con potencia de sobra para tu tarjeta de video.',
    icon: 'Zap',
  },
  {
    key: 'case',
    order: 7,
    label: 'Gabinete',
    categories: ['CASE'],
    required: true,
    multiple: false,
    hint: 'Verificá que la tarjeta de video y el disipador entren en el gabinete que te gusta.',
    icon: 'Box',
  },
  {
    key: 'cooler',
    order: 8,
    label: 'Refrigeración',
    categories: ['COOL'],
    required: false,
    multiple: false,
    hint: 'Si tu procesador no incluye disipador o querés más silencio, sumá refrigeración por aire o líquida.',
    icon: 'Fan',
  },
  {
    key: 'monitor',
    order: 9,
    label: 'Monitor',
    categories: ['MON'],
    required: false,
    multiple: false,
    hint: 'Un monitor de 144 Hz o más aprovecha de verdad una buena tarjeta de video.',
    icon: 'Monitor',
  },
  {
    key: 'peripherals',
    order: 10,
    label: 'Periféricos',
    categories: ['KEY', 'MOU', 'AUD', 'PAD', 'CAM', 'CHA', 'MAND'],
    required: false,
    multiple: true,
    hint: 'Teclado, mouse, audífonos y todo lo que va en tu escritorio.',
    icon: 'Keyboard',
  },
  {
    key: 'software',
    order: 11,
    label: 'Sistema y servicios',
    categories: ['LIC', 'SRV'],
    required: false,
    multiple: true,
    hint: 'Licencia de Windows y el servicio de ensamble para recibir tu PC lista para usar.',
    icon: 'Package',
  },
];

export const REQUIRED_SLOTS: readonly BuildSlot[] = BUILD_SLOTS.filter((slot) => slot.required);

const slotsByKey = new Map<SlotKey, BuildSlot>(BUILD_SLOTS.map((slot) => [slot.key, slot]));
const slotsByCategory = new Map<CategoryCode, BuildSlot>();
for (const slot of BUILD_SLOTS) {
  for (const category of slot.categories) slotsByCategory.set(category, slot);
}

export function slotByKey(key: SlotKey): BuildSlot {
  return slotsByKey.get(key) as BuildSlot;
}

export function isSlotKey(value: string | null | undefined): value is SlotKey {
  return value != null && slotsByKey.has(value as SlotKey);
}

/** Ranura que admite una categoría del catálogo (undefined si no se arma con ella: consolas, laptops, juegos…). */
export function slotForCategory(category: CategoryCode): BuildSlot | undefined {
  return slotsByCategory.get(category);
}

/** Ranura que le corresponde a un producto según su categoría. */
export function slotForProduct(product: Pick<Product, 'category'>): BuildSlot | undefined {
  return slotForCategory(product.category);
}

/** Verdadero si el producto puede sumarse a un armado. */
export function isBuildable(product: Pick<Product, 'category'>): boolean {
  return slotForProduct(product) !== undefined;
}
