// Módulo «Estado del SIAT» · declarar una contingencia manual (`StartManualContingencyCommand`, permiso «gestionar eventos
// significativos, paquetes y CAFC»), como `ManualContingencyDialog` del escritorio: evento del catálogo del SIN (solo los
// de contingencia manual: energía, virus o software, hardware), talonario CAFC activo de la sucursal y hora de inicio. La
// caja deja de emitir en línea: se factura a mano con el talonario y después se transcribe.
// La pantalla lo monta de nuevo en cada apertura (le cambia la `key`).

import { Power } from 'lucide-react';
import { useId, useState } from 'react';
import { useRpcCommand, useRpcQuery } from '@/4-presentation/panel/hooks';
import { Alert, Button, Dialog, Form, FormGrid, SelectField, TextArea, TextField, useNotify } from '@/4-presentation/panel/kit';
import { CATALOG_EVENTS, branchText, cafcOptions, laPazLocalNow, manualEventOptions, plainMessage, toFiscalDateTime, type PointData } from './siat';

export interface ManualContingencyDialogProps {
  /** null = cerrado. */
  target: PointData | null;
  onClose: () => void;
  onDone: () => void;
}

export function ManualContingencyDialog({ target, onClose, onDone }: ManualContingencyDialogProps) {
  const formId = useId();
  const notify = useNotify();
  const [shown] = useState(target);
  const current = target ?? shown;
  const open = target !== null;
  const events = useRpcQuery('GetSiatCatalogQuery', { catalog: CATALOG_EVENTS }, { enabled: open });
  const cafcs = useRpcQuery('GetContingencyCodesQuery', {}, { enabled: open });
  const [eventCode, setEventCode] = useState('');
  const [cafc, setCafc] = useState('');
  const [start, setStart] = useState(() => laPazLocalNow());
  const [description, setDescription] = useState('');
  const [touched, setTouched] = useState(false);
  const declare = useRpcCommand('StartManualContingencyCommand', { notifyError: false });

  if (!current) return null;
  const eventOptions = manualEventOptions(events.data);
  const codeOptions = cafcOptions(cafcs.data, current.branchCode);
  const chosenEvent = eventCode || eventOptions[0]?.value || '';
  const chosenCafc = cafc || codeOptions[0]?.value || '';
  const startedAt = toFiscalDateTime(start);
  const errors = {
    event: chosenEvent ? undefined : 'Elija el evento de la contingencia.',
    cafc: chosenCafc ? undefined : 'Elija el talonario CAFC.',
    start: startedAt ? undefined : 'Escriba la fecha y hora de inicio.',
  };
  const noEvents = events.data !== undefined && eventOptions.length === 0;
  const noCafc = cafcs.data !== undefined && codeOptions.length === 0;

  const submit = async () => {
    setTouched(true);
    if (errors.event || errors.cafc || errors.start || !startedAt) return;
    const trimmed = description.trim();
    const outcome = await declare.run({ pointOfSaleId: current.id, eventCode: Number(chosenEvent), description: trimmed.length > 0 ? trimmed : null, startedAt, cafcCode: chosenCafc });
    if (!outcome.ok) return;
    notify.warning('Contingencia manual declarada', plainMessage(outcome.result));
    onDone();
    onClose();
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      dismissible={!declare.sending}
      size="lg"
      title={`Contingencia manual · ${current.branchCode} · punto ${current.code}`}
      description="La caja deja de emitir en línea: use las facturas del talonario CAFC y transcríbalas en M-INV cuando termine (hasta 72 h después del fin)."
      footer={
        <>
          <Button variant="outline" onClick={onClose} disabled={declare.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} variant="danger" leftIcon={<Power />} loading={declare.sending} disabled={noEvents || noCafc}>
            Declarar contingencia
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={declare.errorText} busy={declare.sending}>
        {noEvents && (
          <Alert tone="danger" title="El catálogo de eventos significativos no está sincronizado">
            Sincronice los catálogos del SIN (Administración › Configuración › Facturación).
          </Alert>
        )}
        {noCafc && (
          <Alert tone="danger" title={`La sucursal ${branchText(current)} no tiene un talonario CAFC activo`}>
            Regístrelo en la pestaña «Talonarios CAFC» y vuelva a intentar.
          </Alert>
        )}
        <FormGrid>
          <SelectField
            label="Evento"
            allLabel={false}
            placeholder={events.data ? 'Elija el evento' : 'Cargando el catálogo…'}
            value={chosenEvent}
            onChange={setEventCode}
            options={eventOptions}
            error={touched ? errors.event : undefined}
            className="sm:col-span-2"
            required
          />
          <SelectField
            label="Talonario CAFC"
            allLabel={false}
            placeholder={cafcs.data ? 'Elija el talonario' : 'Cargando los talonarios…'}
            value={chosenCafc}
            onChange={setCafc}
            options={codeOptions}
            error={touched ? errors.cafc : undefined}
            required
          />
          <TextField
            label="Inicio"
            type="datetime-local"
            value={start}
            onChange={setStart}
            error={touched ? errors.start : undefined}
            hint="Hora de Bolivia."
            required
          />
        </FormGrid>
        <TextArea label="Descripción" value={description} onChange={setDescription} maxLength={500} rows={2} optional hint="Qué pasó (se registra en el evento)." />
      </Form>
    </Dialog>
  );
}
