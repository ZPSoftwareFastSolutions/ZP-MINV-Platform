import '@testing-library/jest-dom/vitest';
import { vi } from 'vitest';

// Las pruebas de la V5 (dominio, aplicación y selectores) corren sobre el mock: ninguna prueba toca la red.
vi.stubEnv('VITE_API_URL', 'mock');
