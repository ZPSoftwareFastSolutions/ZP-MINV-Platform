// USO · Diálogos del panel. Centrados desde 640 px y como hoja inferior en el teléfono. Foco atrapado (Tab y Mayús+Tab
// no salen), Escape y el clic en el fondo cierran, el foco vuelve al botón que lo abrió y el resto de la página queda
// inerte mientras está abierto (misma pila de capas que los cajones: components/ui/modalLayer.ts).
//
//   <Dialog open={abierto} onClose={cerrar} title="Nuevo cliente" footer={<>
//       <Button variant="outline" onClick={cerrar}>Cancelar</Button>
//       <Button type="submit" form="form-cliente" loading={guardar.sending}>Guardar</Button></>}>
//     <Form id="form-cliente" onSubmit={enviar}>…</Form>
//   </Dialog>
//
//   <ConfirmDialog open={confirmar} onClose={() => setConfirmar(false)} tone="danger"
//     title="¿Anular la venta F-CM-000123?" message="Se devuelve el stock y se anula la factura en el SIN."
//     confirmLabel="Anular venta" onConfirm={() => anular.run({ number })} error={anular.errorText} />
//
// El foco entra al primer control, o al que tenga `data-autofocus` (por ejemplo `<TextField data-autofocus … />`).
// ConfirmDialog enfoca «Cancelar» (lo seguro), queda ocupado mientras `onConfirm` trabaja y se cierra solo si termina
// bien: si devuelve false o `{ ok: false }` (lo que devuelve `useRpcCommand().run`) sigue abierto con el error.

import clsx from 'clsx';
import { X } from 'lucide-react';
import { useId, useRef, useState, type ReactNode } from 'react';
import { createPortal } from 'react-dom';
import { Alert } from '@/4-presentation/components/ui/Alert';
import { Button } from '@/4-presentation/components/ui/Button';
import { useModalDialog, useOpenTransition } from '@/4-presentation/components/ui/modalLayer';
import { useStableClose } from './stableClose';

export interface DialogProps {
  open: boolean;
  onClose: () => void;
  title: string;
  description?: ReactNode;
  /** sm 400 px · md 512 px (por defecto) · lg 672 px · xl 896 px. */
  size?: 'sm' | 'md' | 'lg' | 'xl';
  /** Botones al pie (la acción principal a la derecha). */
  footer?: ReactNode;
  children?: ReactNode;
  /** role="alertdialog": el diálogo pide una decisión (confirmaciones). */
  alert?: boolean;
  /** false = ni Escape ni el fondo lo cierran (mientras se guarda). */
  dismissible?: boolean;
  closeLabel?: string;
}

const SIZES = { sm: 'sm:max-w-[25rem]', md: 'sm:max-w-lg', lg: 'sm:max-w-2xl', xl: 'sm:max-w-4xl' } as const;
const CLOSE_MS = 220;

export function Dialog({ open, onClose, title, description, size = 'md', footer, children, alert = false, dismissible = true, closeLabel = 'Cerrar' }: DialogProps) {
  const { mounted, visible } = useOpenTransition(open, CLOSE_MS);
  const panelRef = useRef<HTMLDivElement>(null);
  const titleId = useId();
  const descriptionId = useId();
  // Escape y el fondo: identidad estable, la última `onClose` y solo si se puede cerrar.
  const requestClose = useStableClose(open, onClose, dismissible);
  const { trapFocus } = useModalDialog({ mounted, open, onClose: requestClose, panelRef });

  if (!mounted) return null;

  return createPortal(
    <div className="fixed inset-0 z-50 flex items-end justify-center sm:items-center sm:p-4" aria-hidden={!open || undefined}>
      <div
        className={clsx('absolute inset-0 bg-bg/70 backdrop-blur-sm transition-opacity duration-200', visible ? 'opacity-100' : 'opacity-0')}
        onClick={requestClose}
        data-testid="dialogo-fondo"
      />
      <div
        ref={panelRef}
        role={alert ? 'alertdialog' : 'dialog'}
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={description ? descriptionId : undefined}
        onKeyDown={trapFocus}
        className={clsx(
          'relative flex max-h-[90dvh] w-full flex-col rounded-t-3xl border border-border bg-surface shadow-card will-change-transform sm:max-h-[85dvh] sm:rounded-card',
          'transition-[transform,opacity] duration-200 ease-out',
          SIZES[size],
          visible ? 'translate-y-0 opacity-100' : 'translate-y-6 opacity-0 sm:translate-y-3',
        )}
      >
        <header className="flex items-start justify-between gap-3 border-b border-border px-4 py-3 sm:px-6 sm:py-4">
          <div className="min-w-0">
            <h2 id={titleId} className="font-display text-xl font-semibold text-text">
              {title}
            </h2>
            {description && (
              <div id={descriptionId} className="mt-0.5 text-sm text-text-muted">
                {description}
              </div>
            )}
          </div>
          <button
            type="button"
            aria-label={closeLabel}
            title={closeLabel}
            disabled={!dismissible}
            onClick={onClose}
            className="flex size-11 shrink-0 cursor-pointer items-center justify-center rounded-xl text-text-muted transition-colors duration-200 hover:bg-surface-2 hover:text-text disabled:cursor-not-allowed disabled:opacity-50"
          >
            <X aria-hidden="true" className="size-5" />
          </button>
        </header>
        {children != null && <div className="min-h-0 flex-1 overflow-y-auto overscroll-contain px-4 py-4 sm:px-6">{children}</div>}
        {footer && (
          <footer className="flex flex-col-reverse gap-2 border-t border-border bg-surface-2 px-4 py-3 sm:flex-row sm:justify-end sm:px-6 [&>*]:max-sm:w-full">
            {footer}
          </footer>
        )}
      </div>
    </div>,
    document.body,
  );
}

export interface ConfirmDialogProps {
  open: boolean;
  onClose: () => void;
  /** Pregunta concreta: «¿Anular la venta F-CM-000123?». */
  title: string;
  /** Qué va a pasar. */
  message: ReactNode;
  /** Verbo de la acción: «Anular venta» (por defecto «Confirmar»). */
  confirmLabel?: string;
  cancelLabel?: string;
  /** 'danger' para acciones destructivas (botón rojo); 'primary' para el resto. */
  tone?: 'danger' | 'primary';
  /** La acción. Si devuelve una promesa, espera; si devuelve false o `{ ok: false }`, el diálogo sigue abierto. */
  onConfirm: () => unknown;
  /** Error para mostrar dentro del diálogo (por ejemplo `comando.errorText`). */
  error?: ReactNode;
  confirmDisabled?: boolean;
  /** Contenido extra (un campo «Motivo»). */
  children?: ReactNode;
}

function failed(result: unknown): boolean {
  if (result === false) return true;
  return typeof result === 'object' && result !== null && 'ok' in result && (result as { ok: unknown }).ok === false;
}

export function ConfirmDialog({
  open,
  onClose,
  title,
  message,
  confirmLabel = 'Confirmar',
  cancelLabel = 'Cancelar',
  tone = 'primary',
  onConfirm,
  error,
  confirmDisabled = false,
  children,
}: ConfirmDialogProps) {
  const [busy, setBusy] = useState(false);

  const confirm = async () => {
    setBusy(true);
    try {
      const result = await onConfirm();
      if (!failed(result)) onClose();
    } catch {
      // El error lo muestra quien llamó (`error`); el diálogo sigue abierto para reintentar o cancelar.
    } finally {
      setBusy(false);
    }
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title={title}
      description={message}
      size="sm"
      alert
      dismissible={!busy}
      footer={
        <>
          <Button variant="outline" data-autofocus disabled={busy} onClick={onClose}>
            {cancelLabel}
          </Button>
          <Button variant={tone === 'danger' ? 'danger' : 'primary'} loading={busy} disabled={confirmDisabled} onClick={() => void confirm()}>
            {confirmLabel}
          </Button>
        </>
      }
    >
      {children || error ? (
        <div className="space-y-3">
          {children}
          {error && <Alert tone="danger">{error}</Alert>}
        </div>
      ) : undefined}
    </Dialog>
  );
}
