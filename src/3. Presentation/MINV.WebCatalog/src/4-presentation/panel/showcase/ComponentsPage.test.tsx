// La muestra interna `/panel/_componentes` (solo en desarrollo; las pruebas corren en ese modo) se abre con la tabla de
// rutas REAL, detrás de la guarda del personal, y trae todos los bloques. Las estadísticas empiezan plegadas.

import { fireEvent, screen, within } from '@testing-library/react';
import { describe, expect, it } from 'vitest';
import { renderRoutes, signedInWeb } from '@/test-utils';

describe('muestra interna de componentes del panel', () => {
  it('muestra todos los bloques con datos de ejemplo y las estadísticas plegadas', async () => {
    const web = await signedInWeb('staff');
    const app = await renderRoutes({ web: web.services, route: '/panel/_componentes' });
    expect(await screen.findByRole('heading', { level: 1, name: 'Componentes del panel' }, { timeout: 5000 })).toBeInTheDocument();
    expect(app.location()).toBe('/panel/_componentes');
    for (const title of ['Tablero', 'Pantalla de lista', 'Estados de la tabla', 'Estados sueltos', 'Formulario', 'Diálogos y avisos', 'Distintivos de estado', 'Permisos', 'Servidor']) {
      expect(screen.getByRole('heading', { level: 2, name: title })).toBeInTheDocument();
    }
    const table = screen.getByRole('table', { name: 'Ventas de ejemplo' });
    expect(within(table).getAllByRole('row').length).toBeGreaterThan(20);
    expect(screen.getByText('Mostrando 1–25 de 137')).toBeInTheDocument();

    // Nada de estadísticas al entrar: se montan recién al abrir «Ver estadísticas».
    expect(screen.queryByTestId('estadisticas-montadas')).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Ver estadísticas/ }));
    expect(await screen.findByTestId('estadisticas-montadas')).toBeInTheDocument();
  });

  it('los filtros de la lista quedan en la dirección', async () => {
    const web = await signedInWeb('staff');
    const app = await renderRoutes({ web: web.services, route: '/panel/_componentes?estado=Voided' });
    await screen.findByRole('heading', { level: 1, name: 'Componentes del panel' }, { timeout: 5000 });
    expect(screen.getByTestId('filtros-activos')).toHaveTextContent('1 activo');
    expect((screen.getByRole('combobox', { name: 'Estado' }) as HTMLSelectElement).value).toBe('Voided');
    fireEvent.click(screen.getByRole('button', { name: 'Limpiar filtros' }));
    await screen.findByText('Mostrando 1–25 de 137');
    expect(app.location()).toBe('/panel/_componentes');
  });
});
