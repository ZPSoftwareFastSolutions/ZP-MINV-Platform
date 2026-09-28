import { describe, expect, it } from 'vitest';
import { createSources } from './container';

// La base de la API se resuelve UNA sola vez: «/» (tienda pública detrás del nginx del catálogo) debe quedar como mismo
// origen y no volver al gateway local por una segunda resolución.
describe('createSources', () => {
  it('«/» usa el mismo origen (rutas relativas /storefront/v1)', async () => {
    const sources = await createSources('/');
    expect(sources.mode).toBe('api');
    expect(sources.apiUrl).toBe('');
  });

  it('vacío usa el gateway local; sin argumento manda VITE_API_URL (en las pruebas, el mock)', async () => {
    expect((await createSources('')).apiUrl).toBe('http://localhost:5090');
    expect((await createSources()).mode).toBe('mock');
  });

  it('una URL propia se respeta sin la barra final', async () => {
    expect((await createSources('https://api.tienda.example/')).apiUrl).toBe('https://api.tienda.example');
  });
});
