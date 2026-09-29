// Módulo «Caja» · abrir la caja (`OpenPosSessionCommand`): la caja (preseleccionada la que sugiere el servidor: la del
// turno propio o una libre de la sucursal activa) y el fondo inicial en efectivo. El error del servidor (la caja ya
// tiene un turno abierto de otro cajero, módulo sin licencia…) se muestra dentro del diálogo, sin cerrarlo.

import { LockOpen } from 'lucide-react';
import { useId, useState } from 'react';
import { useRpcCommand } from '@/4-presentation/panel/hooks';
import { Button, Dialog, Form, FormGrid, MoneyField, SelectField } from '@/4-presentation/panel/kit';
import { formatMoney } from '@/4-presentation/panel/lib';
import { OPENING_SUGGESTIONS } from './session';
import type { PosStateData } from './types';

export interface OpenSessionDialogProps {
  open: boolean;
  onClose: () => void;
  state: PosStateData | undefined;
  /** La caja preseleccionada (la sugerida por el servidor). */
  suggested: string;
  onOpened: () => void;
}

export function OpenSessionDialog({ open, onClose, state, suggested, onOpened }: OpenSessionDialogProps) {
  const formId = useId();
  const [register, setRegister] = useState('');
  const [opening, setOpening] = useState<number | null>(null);
  const [touched, setTouched] = useState(false);
  const command = useRpcCommand('OpenPosSessionCommand', {
    success: (_id, payload) => `Caja abierta con un fondo de ${formatMoney(payload.openingCash)}`,
    notifyError: false,
  });

  const registers = state?.registers ?? [];
  const chosen = register || suggested;
  const registerError = touched && !chosen ? 'Elija la caja que va a abrir.' : undefined;
  const openingError = touched && opening === null ? 'Indique el efectivo con el que abre la caja (0 o más).' : undefined;

  const close = () => {
    setRegister('');
    setOpening(null);
    setTouched(false);
    command.reset();
    onClose();
  };

  const submit = async () => {
    setTouched(true);
    if (!chosen || opening === null || opening < 0) return;
    const outcome = await command.run({ registerCode: chosen, openingCash: opening });
    if (outcome.ok) {
      close();
      onOpened();
    }
  };

  return (
    <Dialog
      open={open}
      onClose={close}
      dismissible={!command.sending}
      title="Abrir la caja"
      description={state ? `${state.companyName} · ${state.branchName}` : undefined}
      footer={
        <>
          <Button variant="outline" onClick={close} disabled={command.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<LockOpen />} loading={command.sending}>
            Abrir caja
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={command.errorText} busy={command.sending}>
        <p className="text-sm text-text-muted">Cuente el efectivo del cajón antes de empezar: es el fondo con el que se hará el arqueo al cerrar.</p>
        <FormGrid>
          <SelectField
            label="Caja"
            value={chosen}
            onChange={(value) => {
              setRegister(value);
              command.reset();
            }}
            options={registers.map((option) => ({ value: option.code, label: `${option.name} (${option.code})` }))}
            allLabel={false}
            placeholder="Elija la caja"
            error={registerError}
            required
          />
          <MoneyField
            label="Fondo inicial en efectivo"
            value={opening}
            onChange={(value) => {
              setOpening(value);
              command.reset();
            }}
            error={openingError}
            required
            data-autofocus
          />
        </FormGrid>
        <div className="flex flex-wrap gap-2" role="group" aria-label="Montos frecuentes del fondo inicial">
          {OPENING_SUGGESTIONS.map((amount) => (
            <Button key={amount} variant="subtle" onClick={() => setOpening(amount)} aria-pressed={opening === amount}>
              {formatMoney(amount)}
            </Button>
          ))}
        </div>
      </Form>
    </Dialog>
  );
}
