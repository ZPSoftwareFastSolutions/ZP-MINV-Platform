/// <reference types="vite/client" />

// Variables de entorno que la web reconoce (Vite solo expone las VITE_*).
interface ImportMetaEnv {
  /** Base del API Gateway (`http://localhost:5090` por defecto) o «mock» para usar los datos embebidos de la V5. */
  readonly VITE_API_URL?: string;
}
