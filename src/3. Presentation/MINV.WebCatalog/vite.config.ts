import { fileURLToPath, URL } from 'node:url';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
import { loadEnv } from 'vite';
import { defineConfig } from 'vitest/config';

const ROOT = fileURLToPath(new URL('.', import.meta.url));

// Catálogo web de Tech Zone Gaming (V6): frontend estático (vite build → dist/) que consume la API pública de tienda del
// API Gateway (VITE_API_URL; «mock» usa los datos embebidos de la V5 sin red).
export default defineConfig(({ mode }) => {
  // V7 · La sesión web y el RPC (`/api/v1/web/*`) viajan SIEMPRE al mismo origen de la página: la cookie de la sesión es
  // HttpOnly y SameSite=Strict, y el servidor en la nube no publica CORS. En producción el nginx del catálogo reenvía
  // esa ruta al servidor; en desarrollo (npm run dev / preview) lo hace este proxy hacia MINV_CLOUD_URL (variable del
  // proceso o de .env.local; no llega al navegador). `changeOrigin: false` conserva la cabecera Host del navegador, que
  // el servidor compara con `Origin` como defensa contra CSRF.
  const cloudUrl = process.env.MINV_CLOUD_URL ?? loadEnv(mode, ROOT, 'MINV_').MINV_CLOUD_URL ?? 'http://localhost:5080';
  const webApiProxy = { '/api/v1/web': { target: cloudUrl, changeOrigin: false } };

  return {
    plugins: [react(), tailwindcss()],
    resolve: {
      alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
    },
    server: { port: 5173, proxy: webApiProxy },
    preview: { proxy: webApiProxy },
    build: {
      // El mock (≈ 420 kB) y las bibliotecas van en fragmentos propios: el navegador los guarda en caché aparte del
      // código de la aplicación, que es lo que cambia entre versiones.
      rolldownOptions: {
        output: {
          codeSplitting: {
            groups: [
              { name: 'catalogo', test: /[\\/]3-infrastructure[\\/]data[\\/]/ },
              { name: 'vendor', test: /[\\/]node_modules[\\/]/ },
            ],
          },
        },
      },
    },
    test: {
      environment: 'jsdom',
      globals: true,
      setupFiles: ['./src/test-setup.ts'],
      css: false,
      // V7: las pruebas que montan la aplicación completa descargan el panel entero (27 módulos); con toda la batería en
      // paralelo tardan más que los 5 s por defecto sin estar mal
      testTimeout: 20_000,
    },
  };
});
