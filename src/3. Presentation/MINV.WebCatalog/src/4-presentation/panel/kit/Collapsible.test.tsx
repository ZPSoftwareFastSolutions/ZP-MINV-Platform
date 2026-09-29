// Collapsible «Ver …»: empieza cerrado, su contenido NO se monta (ni carga datos) hasta abrirlo, el símbolo ^ gira y se
// desmonta al cerrar salvo `keepMounted`. Tabs: solo se monta la pestaña elegida.

import { fireEvent, render, screen } from '@testing-library/react';
import { useEffect, useState } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { Collapsible } from './Collapsible';
import { TabPanel, Tabs } from './Tabs';

function Stats({ onMount }: { onMount: () => void }) {
  useEffect(() => {
    onMount();
  }, [onMount]);
  return <p>Ventas del mes: Bs 12.345,00</p>;
}

describe('Collapsible', () => {
  it('cerrado al entrar: el contenido no existe y no se montó', () => {
    const onMount = vi.fn();
    render(
      <Collapsible label="Ver estadísticas" openLabel="Ocultar estadísticas">
        <Stats onMount={onMount} />
      </Collapsible>,
    );
    const button = screen.getByRole('button', { name: 'Ver estadísticas' });
    expect(button).toHaveAttribute('aria-expanded', 'false');
    expect(button).not.toHaveAttribute('aria-controls');
    expect(screen.queryByText(/Ventas del mes/)).not.toBeInTheDocument();
    expect(onMount).not.toHaveBeenCalled();
    expect(screen.getByTestId('plegable-simbolo')).toHaveClass('rotate-180');
    expect(screen.getByRole('heading', { level: 2, name: 'Ver estadísticas' })).toBeInTheDocument();
  });

  it('al abrir se monta (y recién ahí carga), el ^ gira y al cerrar se desmonta', () => {
    const onMount = vi.fn();
    render(
      <Collapsible label="Ver estadísticas" openLabel="Ocultar estadísticas" description="Ventas y productos del mes.">
        <Stats onMount={onMount} />
      </Collapsible>,
    );
    fireEvent.click(screen.getByRole('button', { name: /Ver estadísticas/ }));
    const button = screen.getByRole('button', { name: /Ocultar estadísticas/ });
    expect(button).toHaveAttribute('aria-expanded', 'true');
    const content = screen.getByText(/Ventas del mes/).parentElement!;
    expect(button).toHaveAttribute('aria-controls', content.id);
    expect(onMount).toHaveBeenCalledTimes(1);
    expect(screen.getByTestId('plegable-simbolo')).toHaveClass('rotate-0');

    fireEvent.click(button);
    expect(screen.queryByText(/Ventas del mes/)).not.toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /Ver estadísticas/ }));
    // Cada apertura vuelve a montar (y a cargar) el contenido.
    expect(onMount).toHaveBeenCalledTimes(2);
  });

  it('con keepMounted queda montado (oculto) después de abrirlo una vez', () => {
    const onMount = vi.fn();
    render(
      <Collapsible label="Ver detalle" keepMounted level={3}>
        <Stats onMount={onMount} />
      </Collapsible>,
    );
    expect(onMount).not.toHaveBeenCalled();
    const button = screen.getByRole('button', { name: 'Ver detalle' });
    fireEvent.click(button);
    fireEvent.click(button);
    expect(screen.getByText(/Ventas del mes/, { selector: 'p' }).parentElement).not.toBeVisible();
    fireEvent.click(button);
    expect(onMount).toHaveBeenCalledTimes(1);
    expect(screen.getByRole('heading', { level: 3, name: 'Ver detalle' })).toBeInTheDocument();
  });

  it('controlado desde afuera', () => {
    function Controlled() {
      const [open, setOpen] = useState(true);
      return (
        <Collapsible label="Ver cajas" open={open} onOpenChange={setOpen}>
          <p>Caja 1 abierta</p>
        </Collapsible>
      );
    }
    render(<Controlled />);
    expect(screen.getByText('Caja 1 abierta')).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: 'Ver cajas' }));
    expect(screen.queryByText('Caja 1 abierta')).not.toBeInTheDocument();
  });
});

describe('Tabs del panel', () => {
  function TabsHarness({ onSalesMount }: { onSalesMount: () => void }) {
    const [tab, setTab] = useState<'datos' | 'ventas' | 'notas'>('datos');
    return (
      <Tabs
        label="Secciones del cliente"
        value={tab}
        onChange={setTab}
        tabs={[
          { id: 'datos', label: 'Datos' },
          { id: 'ventas', label: 'Ventas', count: 12 },
          { id: 'notas', label: 'Notas', disabled: true },
        ]}
      >
        <TabPanel id="datos">
          <p>NIT 1234567</p>
        </TabPanel>
        <TabPanel id="ventas">
          <Stats onMount={onSalesMount} />
        </TabPanel>
        <TabPanel id="notas">
          <p>Sin notas</p>
        </TabPanel>
      </Tabs>
    );
  }

  it('solo monta la pestaña elegida y se recorre con las flechas (saltando las deshabilitadas)', () => {
    const onSalesMount = vi.fn();
    render(<TabsHarness onSalesMount={onSalesMount} />);
    const datos = screen.getByRole('tab', { name: 'Datos' });
    expect(datos).toHaveAttribute('aria-selected', 'true');
    expect(screen.getByRole('tabpanel', { name: 'Datos' })).toHaveTextContent('NIT 1234567');
    expect(onSalesMount).not.toHaveBeenCalled();

    fireEvent.keyDown(datos, { key: 'ArrowRight' });
    const ventas = screen.getByRole('tab', { name: /Ventas/ });
    expect(ventas).toHaveAttribute('aria-selected', 'true');
    expect(ventas).toHaveFocus();
    expect(onSalesMount).toHaveBeenCalledTimes(1);
    expect(screen.queryByText('NIT 1234567')).not.toBeInTheDocument();

    fireEvent.keyDown(ventas, { key: 'ArrowRight' });
    expect(screen.getByRole('tab', { name: 'Datos' })).toHaveAttribute('aria-selected', 'true');
    fireEvent.keyDown(screen.getByRole('tab', { name: 'Datos' }), { key: 'End' });
    expect(screen.getByRole('tab', { name: /Ventas/ })).toHaveAttribute('aria-selected', 'true');
  });
});
