// Dialog y ConfirmDialog: el foco entra al abrir, Tab y Mayús+Tab no salen, Escape y el fondo cierran, el foco vuelve
// al botón que lo abrió; la confirmación enfoca «Cancelar», espera a la acción y sigue abierta si falla.

import { fireEvent, render, screen, waitFor, within } from '@testing-library/react';
import { useState } from 'react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { ConfirmDialog, Dialog } from './Dialog';
import { SearchField } from './SearchField';

// jsdom no calcula el diseño (offsetParent siempre es null) y la trampa del foco solo recorre controles visibles: aquí
// todo control conectado cuenta como visible.
const offsetParent = Object.getOwnPropertyDescriptor(HTMLElement.prototype, 'offsetParent');

beforeEach(() => {
  Object.defineProperty(HTMLElement.prototype, 'offsetParent', {
    configurable: true,
    get(this: HTMLElement) {
      return this.isConnected ? document.body : null;
    },
  });
});

afterEach(() => {
  if (offsetParent) Object.defineProperty(HTMLElement.prototype, 'offsetParent', offsetParent);
});

function DialogHarness({ dismissible = true, autofocusField = false }: { dismissible?: boolean; autofocusField?: boolean }) {
  const [open, setOpen] = useState(false);
  const [search, setSearch] = useState('');
  return (
    <>
      <button type="button" onClick={() => setOpen(true)}>
        Nuevo cliente
      </button>
      <Dialog
        open={open}
        onClose={() => setOpen(false)}
        title="Nuevo cliente"
        description="Los datos se usan en la factura."
        dismissible={dismissible}
        footer={
          <>
            <button type="button" onClick={() => setOpen(false)}>
              Cancelar
            </button>
            <button type="button">Guardar</button>
          </>
        }
      >
        <label>
          Nombre
          <input data-autofocus={autofocusField || undefined} />
        </label>
        <SearchField label="Buscar cliente" value={search} onChange={setSearch} />
      </Dialog>
    </>
  );
}

async function openDialog() {
  const trigger = screen.getByRole('button', { name: 'Nuevo cliente' });
  trigger.focus();
  fireEvent.click(trigger);
  const dialog = await screen.findByRole('dialog', { name: 'Nuevo cliente' });
  return { trigger, dialog };
}

describe('Dialog', () => {
  it('el foco entra al abrir y queda atrapado con Tab y Mayús+Tab', async () => {
    render(<DialogHarness />);
    const { dialog } = await openDialog();
    expect(dialog).toHaveAttribute('aria-modal', 'true');
    expect(dialog).toHaveAccessibleDescription('Los datos se usan en la factura.');
    const close = within(dialog).getByRole('button', { name: 'Cerrar' });
    await waitFor(() => expect(close).toHaveFocus());

    const save = within(dialog).getByRole('button', { name: 'Guardar' });
    save.focus();
    fireEvent.keyDown(save, { key: 'Tab' });
    expect(close).toHaveFocus();
    fireEvent.keyDown(close, { key: 'Tab', shiftKey: true });
    expect(save).toHaveFocus();
  });

  it('Escape cierra y el foco vuelve al botón que lo abrió', async () => {
    render(<DialogHarness />);
    const { trigger, dialog } = await openDialog();
    await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Cerrar' })).toHaveFocus());
    fireEvent.keyDown(document.activeElement ?? document.body, { key: 'Escape' });
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
    await waitFor(() => expect(trigger).toHaveFocus());
  });

  it('el clic en el fondo cierra; Escape dentro de una búsqueda con texto solo la borra', async () => {
    render(<DialogHarness />);
    const { dialog } = await openDialog();
    const search = within(dialog).getByRole('searchbox', { name: 'Buscar cliente' });
    fireEvent.change(search, { target: { value: 'ana' } });
    fireEvent.keyDown(search, { key: 'Escape' });
    expect(search).toHaveValue('');
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    fireEvent.click(screen.getByTestId('dialogo-fondo'));
    await waitFor(() => expect(screen.queryByRole('dialog')).not.toBeInTheDocument());
  });

  it('con `data-autofocus` el foco va a ese campo', async () => {
    render(<DialogHarness autofocusField />);
    const { dialog } = await openDialog();
    await waitFor(() => expect(within(dialog).getByRole('textbox', { name: 'Nombre' })).toHaveFocus());
  });

  it('sin `dismissible` ni Escape ni el fondo lo cierran', async () => {
    render(<DialogHarness dismissible={false} />);
    const { dialog } = await openDialog();
    fireEvent.keyDown(dialog, { key: 'Escape' });
    fireEvent.click(screen.getByTestId('dialogo-fondo'));
    expect(screen.getByRole('dialog')).toBeInTheDocument();
    expect(within(dialog).getByRole('button', { name: 'Cerrar' })).toBeDisabled();
  });
});

function ConfirmHarness({ onConfirm }: { onConfirm: () => unknown }) {
  const [open, setOpen] = useState(false);
  return (
    <>
      <button type="button" onClick={() => setOpen(true)}>
        Anular
      </button>
      <ConfirmDialog
        open={open}
        onClose={() => setOpen(false)}
        tone="danger"
        title="¿Anular la venta F-CM-000123?"
        message="Se devuelve el stock y se anula la factura."
        confirmLabel="Anular venta"
        onConfirm={onConfirm}
        error={open ? 'Último error: sin respuesta.' : null}
      />
    </>
  );
}

describe('ConfirmDialog', () => {
  it('es un alertdialog que enfoca «Cancelar» y cancelar no ejecuta nada', async () => {
    const onConfirm = vi.fn();
    render(<ConfirmHarness onConfirm={onConfirm} />);
    fireEvent.click(screen.getByRole('button', { name: 'Anular' }));
    const dialog = await screen.findByRole('alertdialog', { name: '¿Anular la venta F-CM-000123?' });
    expect(dialog).toHaveAccessibleDescription('Se devuelve el stock y se anula la factura.');
    const cancel = within(dialog).getByRole('button', { name: 'Cancelar' });
    await waitFor(() => expect(cancel).toHaveFocus());
    fireEvent.click(cancel);
    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument());
    expect(onConfirm).not.toHaveBeenCalled();
  });

  it('espera a la acción (ocupado, sin cerrarse con Escape) y se cierra si termina bien', async () => {
    let finish: (value: unknown) => void = () => undefined;
    const onConfirm = vi.fn(() => new Promise((resolve) => (finish = resolve)));
    render(<ConfirmHarness onConfirm={onConfirm} />);
    fireEvent.click(screen.getByRole('button', { name: 'Anular' }));
    const dialog = await screen.findByRole('alertdialog');
    const confirm = within(dialog).getByRole('button', { name: 'Anular venta' });
    fireEvent.click(confirm);
    expect(onConfirm).toHaveBeenCalledTimes(1);
    await waitFor(() => expect(confirm).toHaveAttribute('aria-busy', 'true'));
    fireEvent.keyDown(dialog, { key: 'Escape' });
    expect(screen.getByRole('alertdialog')).toBeInTheDocument();
    finish({ ok: true, result: null, replayed: false });
    await waitFor(() => expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument());
  });

  it('si la acción falla (`{ ok: false }` o false) sigue abierto con el error', async () => {
    const onConfirm = vi.fn().mockResolvedValueOnce({ ok: false }).mockResolvedValueOnce(false);
    render(<ConfirmHarness onConfirm={onConfirm} />);
    fireEvent.click(screen.getByRole('button', { name: 'Anular' }));
    const dialog = await screen.findByRole('alertdialog');
    expect(within(dialog).getByRole('alert')).toHaveTextContent('Último error: sin respuesta.');
    fireEvent.click(within(dialog).getByRole('button', { name: 'Anular venta' }));
    await waitFor(() => expect(onConfirm).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Anular venta' })).not.toHaveAttribute('aria-busy'));
    expect(screen.getByRole('alertdialog')).toBeInTheDocument();
    fireEvent.click(within(dialog).getByRole('button', { name: 'Anular venta' }));
    await waitFor(() => expect(onConfirm).toHaveBeenCalledTimes(2));
    await waitFor(() => expect(within(dialog).getByRole('button', { name: 'Anular venta' })).not.toHaveAttribute('aria-busy'));
    expect(screen.getByRole('alertdialog')).toBeInTheDocument();
  });
});
