// Módulo «Caja» · cerrar la caja con el arqueo (`ClosePosSessionCommand`): el efectivo esperado (fondo + ventas en
// efectivo), el contado que escribe el cajero y la diferencia (sobrante o faltante), con confirmación: el turno cerrado
// no se vuelve a abrir. La diferencia que queda registrada es la que calcula el servidor (se avisa al terminar).

import { Lock } from 'lucide-react';
import { useState } from 'react';
import { useRpcCommand } from '@/4-presentation/panel/hooks';
import { Alert, Button, ConfirmDialog, DetailList, MoneyField, useNotify } from '@/4-presentation/panel/kit';
import { formatMoney } from '@/4-presentation/panel/lib';
import { cashDifference, closedNotice, plural, registerLabel } from './session';
import type { PosSessionData } from './types';

export interface CloseSessionDialogProps {
  open: boolean;
  onClose: () => void;
  session: PosSessionData | null;
  onClosed: () => void;
}

export function CloseSessionDialog({ open, onClose, session, onClosed }: CloseSessionDialogProps) {
  const notify = useNotify();
  const [counted, setCounted] = useState<number | null>(null);
  const command = useRpcCommand('ClosePosSessionCommand', { notifyError: false });
  // El último turno sigue a la vista mientras el diálogo se cierra (animación de salida).
  const [shown, setShown] = useState<PosSessionData | null>(session);
  if (session && session !== shown) setShown(session);

  const close = () => {
    setCounted(null);
    command.reset();
    onClose();
  };

  const confirm = async () => {
    if (!shown || counted === null) return false;
    const outcome = await command.run({ sessionId: shown.id, countedCash: counted });
    if (!outcome.ok) return outcome;
    const notice = closedNotice(outcome.result);
    if (notice.tone === 'success') notify.success('Caja cerrada', notice.description);
    else notify.warning('Caja cerrada', notice.description);
    setCounted(null);
    onClosed();
    return outcome;
  };

  const difference = shown && counted !== null ? cashDifference(counted, shown.expectedCash) : null;

  return (
    <ConfirmDialog
      open={open}
      onClose={close}
      tone="danger"
      title="¿Cerrar la caja?"
      message={
        shown
          ? `${registerLabel(shown)}. Cuente el efectivo del cajón y escríbalo: el turno se cierra con este arqueo y no se puede volver a abrir.`
          : 'Cuente el efectivo del cajón y escríbalo.'
      }
      confirmLabel="Cerrar caja"
      confirmDisabled={counted === null}
      onConfirm={confirm}
      error={command.errorText}
    >
      {shown && (
        <div className="space-y-3" data-testid="arqueo">
          <DetailList
            columns={1}
            items={[
              { label: 'Fondo inicial', value: formatMoney(shown.openingCash) },
              { label: 'Ventas en efectivo', value: formatMoney(shown.cashSales) },
              { label: 'Efectivo esperado', value: <span className="font-semibold">{formatMoney(shown.expectedCash)}</span> },
              { label: 'Ventas del turno', value: `${plural(shown.tickets, 'venta', 'ventas')} por ${formatMoney(shown.sales)}` },
            ]}
          />
          <MoneyField
            label="Efectivo contado"
            value={counted}
            onChange={(value) => {
              setCounted(value);
              command.reset();
            }}
            required
          />
          <Button variant="subtle" leftIcon={<Lock />} onClick={() => setCounted(shown.expectedCash)}>
            Contó lo esperado ({formatMoney(shown.expectedCash)})
          </Button>
          {difference && (
            <Alert tone={difference.tone} title="Diferencia del arqueo">
              {difference.text}
            </Alert>
          )}
        </div>
      )}
    </ConfirmDialog>
  );
}
