// Datos de ejemplo del contrato (JSON de docs/integration/storefront-api-v1.md, recortados) para las pruebas del mapeo,
// del adaptador HTTP y de la presentación. Solo lo importan las pruebas.

import type { StorefrontCatalogDto, StorefrontProblemDto, StorefrontProductDto, StorefrontReservationViewDto } from './dto';

export const CASE_DTO: StorefrontProductDto = {
  sku: 'CASE-COR-4000D',
  slug: 'case-cor-4000d',
  name: 'Gabinete Corsair 4000D Airflow negro',
  shortName: 'Gabinete Corsair 4000D Airflow negro',
  category: 'CASE',
  categoryName: 'Gabinetes',
  categoryPath: 'Componentes > Gabinetes',
  brand: 'Corsair',
  price: 1299.0,
  listPrice: null,
  image: '/storefront/v1/products/CASE-COR-4000D/image',
  available: 4,
  reserved: 0,
  onHand: 4,
  condition: 'Nuevo',
  warrantyMonths: 12,
  serialized: false,
  popularity: 7,
  tags: ['nuevo'],
  description: 'Gabinete Corsair 4000D Airflow negro. Marca Corsair · Componentes › Gabinetes.',
  highlights: ['Formatos de placa soportados: ATX, Micro-ATX, Mini-ITX', 'Tipo: Mid Tower', 'Color: Negro'],
  specs: [
    { key: 'condicion', label: 'Condición', value: 'Nuevo', text: 'Nuevo', unit: null, filterable: true },
    { key: 'formatos_placa', label: 'Formatos de placa soportados', value: ['ATX', 'Micro-ATX', 'Mini-ITX'], text: 'ATX, Micro-ATX, Mini-ITX', unit: null, filterable: true },
    { key: 'largo_max_gpu', label: 'Largo máximo de GPU', value: 360, text: '360 mm', unit: 'mm', filterable: false },
  ],
};

export const CPU_DTO: StorefrontProductDto = {
  ...CASE_DTO,
  sku: 'CPU-AMD-7600',
  slug: 'cpu-amd-7600',
  name: 'Procesador AMD Ryzen 5 7600 (AM5, 6 núcleos, 5,1 GHz, con disipador)',
  shortName: 'Procesador AMD Ryzen 5 7600',
  category: 'CPU',
  categoryName: 'Procesadores',
  categoryPath: 'Componentes > Procesadores',
  brand: 'AMD',
  price: 2049.0,
  listPrice: 2199.0,
  image: null,
  available: 0,
  reserved: 2,
  onHand: 2,
  popularity: 10,
  tags: ['destacado', 'oferta', 'rara'],
  highlights: [],
  specs: [{ key: 'socket', label: 'Socket', value: 'AM5', text: 'AM5', unit: null, filterable: true }],
};

export const CATALOG_DTO: StorefrontCatalogDto = {
  company: {
    code: 'TECHZONE',
    name: 'Tech Zone Gaming S.R.L.',
    branches: [
      { code: 'CB', name: 'Sucursal Cochabamba' },
      { code: 'CM', name: 'Casa matriz La Paz · Av. 16 de Julio (El Prado)' },
      { code: 'SC', name: 'Sucursal Santa Cruz' },
    ],
  },
  branch: { code: 'CM', name: 'Casa matriz La Paz · Av. 16 de Julio (El Prado)' },
  categories: [
    { code: 'COMP', name: 'Componentes', slug: 'componentes', parent: null, icon: 'Cpu', description: 'Componentes para armar o actualizar tu PC', productCount: 2 },
    { code: 'CPU', name: 'Procesadores', slug: 'procesadores', parent: 'COMP', icon: 'Cpu', description: 'Componentes > Procesadores', productCount: 1 },
    { code: 'CASE', name: 'Gabinetes', slug: 'gabinetes', parent: 'COMP', icon: 'Box', description: 'Componentes > Gabinetes', productCount: 1 },
  ],
  brands: [
    { code: 'AMD', name: 'AMD', productCount: 1 },
    { code: 'CORSAIR', name: 'Corsair', productCount: 1 },
  ],
  products: [CASE_DTO, CPU_DTO],
  presets: [
    {
      id: 'arm-cm-000001',
      number: 'ARM-CM-000001',
      name: 'PC Gamer Entrada 1080p (Core i5-14400F + RTX 4060)',
      tier: 'entrada',
      total: 3348.0,
      available: true,
      lines: [
        { slot: 'cpu', sku: 'CPU-AMD-7600', quantity: 1, unitPrice: 2049.0 },
        { slot: 'case', sku: 'CASE-COR-4000D', quantity: 1, unitPrice: 1299.0 },
        { slot: 'ranura-desconocida', sku: 'X', quantity: 1, unitPrice: 1 },
      ],
    },
  ],
  generatedAt: '2026-09-27T18:39:56.0601584+00:00',
};

export const RESERVATION_DTO: StorefrontReservationViewDto = {
  number: 'ARM-WEB-000004',
  status: 'Reserved',
  statusText: 'Reservada',
  createdAt: '2026-09-27T18:39:56.7982846+00:00',
  reservedUntil: '2026-09-29T18:39:56.7982846+00:00',
  total: 3348.0,
  contactName: 'Valentina Aguirre',
  branch: 'CM',
  notes: 'Paso el sabado por la manana',
  hasCompatibilityWarnings: false,
  lines: [
    { slot: 'cpu', sku: 'CPU-AMD-7600', name: 'Procesador AMD Ryzen 5 7600 (AM5, 6 núcleos, 5,1 GHz, con disipador)', quantity: 1, unitPrice: 2049.0, subtotal: 2049.0 },
    { slot: 'case', sku: 'CASE-COR-4000D', name: 'Gabinete Corsair 4000D Airflow negro', quantity: 1, unitPrice: 1299.0, subtotal: 1299.0 },
  ],
  cancelReason: null,
};

export const INSUFFICIENT_STOCK_PROBLEM: StorefrontProblemDto = {
  type: 'https://minv.example/errores/storefront.insufficient_stock',
  title: 'insufficient_stock',
  status: 409,
  detail: 'No hay stock suficiente para 1 pieza(s): CASE-COR-4000D (pedido 16, disponible 4)',
  code: 'storefront.insufficient_stock',
  shortages: [{ sku: 'CASE-COR-4000D', name: 'Gabinete Corsair 4000D Airflow negro', requested: 16, available: 4 }],
  traceId: '00-2603809486bd5f7da4e79cdffaa494af-d26e11a8107fb49e-00',
};
