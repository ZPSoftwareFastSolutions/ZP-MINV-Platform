// CatalogProvider: cargando (esqueleto), error con «Reintentar» y listo (los hijos ven los servicios); el refresco en
// segundo plano conserva la instantánea anterior si falla.

import { act, fireEvent, render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';
import type { ICatalogSource } from '@/1-domain/ports/ICatalogSource';
import { StorefrontError } from '@/1-domain/storefront/errors';
import type { Sources } from '@/4-presentation/app/container';
import { useServices } from '@/4-presentation/hooks/useServices';
import { MockCatalogSource, mockSnapshot } from '@/3-infrastructure/data/mockCatalog';
import { mockSources } from '@/test-utils';
import { CatalogProvider } from './CatalogProvider';

function Probe() {
  const { catalog, mode, generatedAt } = useServices();
  return (
    <p data-testid="sonda">
      {mode} · {catalog.searchCatalog({ pageSize: 1 }).total} productos · {catalog.getStore().branch.code} · {generatedAt.toISOString()}
    </p>
  );
}

function sourcesWith(source: ICatalogSource): Sources {
  return { ...mockSources(), source };
}

describe('CatalogProvider', () => {
  it('muestra el esqueleto mientras carga y después entrega los servicios a los hijos', async () => {
    render(
      <CatalogProvider sources={mockSources()}>
        <Probe />
      </CatalogProvider>,
    );
    expect(screen.getByTestId('catalogo-cargando')).toBeInTheDocument();
    const probe = await screen.findByTestId('sonda');
    expect(probe).toHaveTextContent(/mock · 159 productos · CM/);
    expect(screen.queryByTestId('catalogo-cargando')).not.toBeInTheDocument();
  });

  it('acepta la promesa del origen (el mock se carga en un fragmento aparte)', async () => {
    render(
      <CatalogProvider sources={Promise.resolve(mockSources())}>
        <Probe />
      </CatalogProvider>,
    );
    expect(await screen.findByTestId('sonda')).toHaveTextContent(/159 productos/);
  });

  it('con la tienda caída muestra el error con «Reintentar» y se recupera al reintentar', async () => {
    const load = vi
      .fn<() => Promise<ReturnType<typeof mockSnapshot>>>()
      .mockRejectedValueOnce(new StorefrontError({ kind: 'network', status: 0, detail: 'No hubo respuesta de la tienda.' }))
      .mockResolvedValue(mockSnapshot());
    const source: ICatalogSource = { load, product: async () => undefined, presets: async () => [] };
    render(
      <CatalogProvider sources={sourcesWith(source)}>
        <Probe />
      </CatalogProvider>,
    );
    const error = await screen.findByTestId('catalogo-error');
    expect(error).toHaveTextContent('No pudimos cargar el catálogo');
    expect(screen.getByRole('alert')).toHaveTextContent(/conexión/);
    fireEvent.click(screen.getByRole('button', { name: 'Reintentar' }));
    expect(await screen.findByTestId('sonda')).toHaveTextContent(/159 productos/);
    expect(load).toHaveBeenCalledTimes(2);
  });

  it('un 503 muestra el detalle del servidor', async () => {
    const source: ICatalogSource = {
      load: async () => {
        throw new StorefrontError({ kind: 'unavailable', status: 503, detail: 'La tienda no está configurada en el servidor.' });
      },
      product: async () => undefined,
      presets: async () => [],
    };
    render(
      <CatalogProvider sources={sourcesWith(source)}>
        <Probe />
      </CatalogProvider>,
    );
    expect(await screen.findByTestId('catalogo-error')).toHaveTextContent('La tienda no está configurada en el servidor.');
  });

  it('refresca en segundo plano cada `refreshMs` sin parpadear y conserva la instantánea si el refresco falla', async () => {
    vi.useFakeTimers();
    try {
      const first = mockSnapshot();
      first.generatedAt = new Date('2026-09-27T10:00:00Z');
      const second = mockSnapshot();
      second.generatedAt = new Date('2026-09-27T10:01:00Z');
      const load = vi
        .fn<() => Promise<ReturnType<typeof mockSnapshot>>>()
        .mockResolvedValueOnce(first)
        .mockResolvedValueOnce(second)
        .mockRejectedValueOnce(new StorefrontError({ kind: 'network', status: 0, detail: 'sin red' }));
      const source: ICatalogSource = { load, product: (slug) => new MockCatalogSource().product(slug), presets: async () => [] };
      render(
        <CatalogProvider sources={sourcesWith(source)} refreshMs={1000}>
          <Probe />
        </CatalogProvider>,
      );
      await act(async () => {
        await vi.advanceTimersByTimeAsync(0);
      });
      expect(screen.getByTestId('sonda')).toHaveTextContent('2026-09-27T10:00:00.000Z');

      await act(async () => {
        await vi.advanceTimersByTimeAsync(1000);
      });
      expect(screen.getByTestId('sonda')).toHaveTextContent('2026-09-27T10:01:00.000Z');
      expect(screen.queryByTestId('catalogo-cargando')).not.toBeInTheDocument();

      await act(async () => {
        await vi.advanceTimersByTimeAsync(1000);
      });
      expect(load).toHaveBeenCalledTimes(3);
      expect(screen.getByTestId('sonda')).toHaveTextContent('2026-09-27T10:01:00.000Z');
      expect(screen.queryByTestId('catalogo-error')).not.toBeInTheDocument();
    } finally {
      vi.useRealTimers();
    }
  });
});
