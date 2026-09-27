// Datos institucionales de ejemplo del sitio de demostración. Nada de aquí es un dato real de una persona.

export const STORE = {
  legalName: 'Tech Zone Gaming S.R.L.',
  shortName: 'Tech Zone',
  wordmark: 'TECH ZONE',
  tagline: 'Componentes, consolas y periféricos con garantía oficial en toda Bolivia.',
  phone: '+591 2 2000000',
  whatsapp: '+591 70000000',
  whatsappUrl: 'https://wa.me/59170000000',
  email: 'ventas@techzone.example',
  address: 'Av. Ejemplo 1234, Sopocachi',
  city: 'La Paz, Bolivia',
  hours: 'Lun. a vie. 9:00 a 19:00 · Sáb. 9:00 a 13:00',
  branches: ['La Paz', 'Cochabamba', 'Santa Cruz'],
  year: 2026,
} as const;

/** Escala de capas (z-index): cabecera < menús desplegables < cajones < avisos. */
export const Z_INDEX = {
  header: 30,
  dropdown: 40,
  drawer: 50,
  toast: 60,
} as const;

export const PAGE_SIZE_DEFAULT = 24;
