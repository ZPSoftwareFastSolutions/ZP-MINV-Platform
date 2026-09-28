// Utilidades de las pruebas de la presentación: orígenes en memoria (mock de la V5 con la pasarela de reservas) y un
// render con todos los proveedores (catálogo → avisos → armado → enrutador en memoria). Solo lo importan las pruebas.

import { render, screen, type RenderResult } from '@testing-library/react';
import type { ReactNode } from 'react';
import { MemoryRouter, Route, Routes } from 'react-router-dom';
import { createMockSources, type Sources } from '@/4-presentation/app/container';
import { ToastProvider } from '@/4-presentation/components/feedback/ToastProvider';
import { BuilderProvider } from '@/4-presentation/state/BuilderProvider';
import { CatalogProvider } from '@/4-presentation/state/CatalogProvider';
import { InMemoryReservationGateway, MOCK_CATALOG, MockCatalogSource } from '@/3-infrastructure/data/mockCatalog';
import type { InMemoryCatalogData } from '@/3-infrastructure/InMemoryCatalogRepository';

/** Orígenes en memoria sobre el mock (o sobre datos propios): la fuente ve lo que reserva la pasarela. */
export function mockSources(data: InMemoryCatalogData = MOCK_CATALOG): Sources {
  return createMockSources(data, { MockCatalogSource, InMemoryReservationGateway });
}

export interface RenderAppOptions {
  sources?: Sources | Promise<Sources>;
  /** Ruta inicial del enrutador en memoria. */
  route?: string;
  /** Patrón de la ruta que dibuja `ui` (por ejemplo `/reserva/:numero`); por defecto cualquiera. */
  path?: string;
}

/** Dibuja `ui` dentro de la aplicación completa y espera a que el catálogo esté listo. */
export async function renderWithApp(ui: ReactNode, { sources = mockSources(), route = '/', path = '*' }: RenderAppOptions = {}): Promise<RenderResult> {
  const result = render(
    <CatalogProvider sources={sources}>
      <ToastProvider>
        <BuilderProvider>
          <MemoryRouter initialEntries={[route]}>
            <Routes>
              <Route path={path} element={withReadyMark(ui)} />
            </Routes>
          </MemoryRouter>
        </BuilderProvider>
      </ToastProvider>
    </CatalogProvider>,
  );
  await screen.findByTestId('catalogo-listo');
  return result;
}

/** Marca invisible que las pruebas esperan para saber que el catálogo ya cargó (no es un componente: es un fragmento). */
function withReadyMark(ui: ReactNode): ReactNode {
  return (
    <>
      <span data-testid="catalogo-listo" hidden />
      {ui}
    </>
  );
}
