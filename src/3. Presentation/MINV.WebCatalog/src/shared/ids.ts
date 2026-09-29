// Identificadores únicos sin coordinación (UUID v4). Función pura de utilidad: sin React ni capas.

const UUID_PATTERN = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function randomBytes(length: number): Uint8Array {
  const bytes = new Uint8Array(length);
  const cryptoApi = globalThis.crypto;
  if (cryptoApi && typeof cryptoApi.getRandomValues === 'function') return cryptoApi.getRandomValues(bytes);
  // Sin Web Crypto (entornos muy viejos): azar suficiente para no repetir un pedido.
  for (let index = 0; index < length; index += 1) bytes[index] = Math.floor(Math.random() * 256);
  return bytes;
}

/** UUID v4 («4f6a0c2e-8d1b-4c3e-9a7f-5b2d1e0c9a8b»): el servidor exige un Guid como `requestId` del RPC. */
export function newUuid(): string {
  const cryptoApi = globalThis.crypto;
  if (cryptoApi && typeof cryptoApi.randomUUID === 'function') return cryptoApi.randomUUID();
  const bytes = randomBytes(16);
  bytes[6] = (bytes[6] & 0x0f) | 0x40;
  bytes[8] = (bytes[8] & 0x3f) | 0x80;
  const hex = Array.from(bytes, (byte) => byte.toString(16).padStart(2, '0'));
  return `${hex.slice(0, 4).join('')}-${hex.slice(4, 6).join('')}-${hex.slice(6, 8).join('')}-${hex.slice(8, 10).join('')}-${hex.slice(10).join('')}`;
}

export function isUuid(value: string): boolean {
  return UUID_PATTERN.test(value);
}
