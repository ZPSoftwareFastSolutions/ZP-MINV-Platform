import { fileURLToPath, URL } from 'node:url';
import react from '@vitejs/plugin-react';
import tailwindcss from '@tailwindcss/vite';
import { defineConfig } from 'vitest/config';

// Catálogo web de Tech Zone Gaming (V6): frontend estático (vite build → dist/) que consume la API pública de tienda del
// API Gateway (VITE_API_URL; «mock» usa los datos embebidos de la V5 sin red).
export default defineConfig({
  plugins: [react(), tailwindcss()],
  resolve: {
    alias: { '@': fileURLToPath(new URL('./src', import.meta.url)) },
  },
  server: { port: 5173 },
  build: {
    // El mock (≈ 420 kB) y las bibliotecas van en fragmentos propios: el navegador los guarda en caché aparte del código
    // de la aplicación, que es lo que cambia entre versiones.
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
  },
});
