// Módulo «Documentos fiscales» · anular un documento ante el SIN (`VoidFiscalDocumentCommand`, permiso «anular y revertir
// documentos fiscales»), como `VoidFiscalDialog` del escritorio: motivo del catálogo del SIN (MOTIVOS_ANULACION) en una
// lista desplegable, el plazo del día 9 que informa el servidor a la vista, «anular y devolver la mercadería» EXPLÍCITO
// (solo facturas de una venta) y confirmación expresa. El error del servidor se muestra dentro del diálogo.
// La pantalla lo monta de nuevo en cada apertura (le cambia la `key`).

import { Ban } from 'lucide-react';
import { useId, useState } from 'react';
import { useRpcCommand, useRpcQuery } from '@/4-presentation/panel/hooks';
import { Alert, Button, Checkbox, Dialog, Form, SelectField, TextArea, useNotify } from '@/4-presentation/panel/kit';
import { formatMoney } from '@/4-presentation/panel/lib';
import { CATALOG_VOID_REASONS, canReturnGoods, formatFiscalDate, plainMessage, preferredReason, voidReasonOptions, type DocumentRecord } from './fiscal';

export interface VoidDialogProps {
  /** null = cerrado. */
  target: DocumentRecord | null;
  onClose: () => void;
  onDone: () => void;
}

export function VoidDialog({ target, onClose, onDone }: VoidDialogProps) {
  const formId = useId();
  const notify = useNotify();
  const [shown] = useState(target);
  const current = target ?? shown;
  const open = target !== null;
  const reasons = useRpcQuery('GetSiatCatalogQuery', { catalog: CATALOG_VOID_REASONS }, { enabled: open });
  const [reason, setReason] = useState('');
  const [returnGoods, setReturnGoods] = useState(false);
  const [confirmed, setConfirmed] = useState(false);
  const [note, setNote] = useState('');
  const [touched, setTouched] = useState(false);
  const voiding = useRpcCommand('VoidFiscalDocumentCommand', { notifyError: false });

  if (!current) return null;
  const options = voidReasonOptions(reasons.data);
  const chosen = reason || preferredReason(reasons.data, current.kind);
  const returnable = canReturnGoods(current);
  const noReasons = reasons.data !== undefined && options.length === 0;
  const errors = {
    reason: chosen === '' ? 'Elija el motivo de la anulación.' : undefined,
    confirmed: confirmed ? undefined : 'Marque la confirmación para anular.',
  };

  const submit = async () => {
    setTouched(true);
    if (errors.reason || errors.confirmed || noReasons) return;
    const trimmed = note.trim();
    const outcome = await voiding.run({ documentId: current.id, reasonCode: Number(chosen), returnGoods: returnable && returnGoods, note: trimmed.length > 0 ? trimmed : null });
    if (!outcome.ok) return;
    notify.success('Documento anulado', plainMessage(outcome.result));
    onDone();
    onClose();
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      dismissible={!voiding.sending}
      alert
      title={`Anular ${current.kind === 'Invoice' ? 'la factura' : 'la nota'} N° ${current.number}`}
      description={`${current.buyerName} · ${current.buyerDocument} · ${formatMoney(current.total)}`}
      footer={
        <>
          <Button variant="outline" data-autofocus onClick={onClose} disabled={voiding.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} variant="danger" leftIcon={<Ban />} loading={voiding.sending} disabled={noReasons}>
            Anular ante el SIN
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={voiding.errorText} busy={voiding.sending}>
        <Alert tone="warning" title="La anulación es ante el SIN">
          Se puede anular hasta el {formatFiscalDate(current.voidDeadline)} a las 23:59 (día 9 del mes siguiente a la emisión). El comprador recibe el aviso por
          correo.
        </Alert>
        {noReasons && (
          <Alert tone="danger" title="Falta el catálogo de motivos de anulación">
            Sincronice los catálogos del SIN (Administración › Configuración › Facturación) y vuelva a intentar.
          </Alert>
        )}
        <SelectField
          label="Motivo"
          allLabel={false}
          placeholder={reasons.data ? 'Elija el motivo' : 'Cargando los motivos del SIN…'}
          value={chosen}
          onChange={(value) => {
            setReason(value);
            if (voiding.error) voiding.reset();
          }}
          options={options}
          error={touched ? errors.reason : undefined}
          hint={reasons.error ? 'No se pudo leer el catálogo de motivos: cierre y vuelva a intentar.' : 'Del catálogo del SIN (MOTIVOS_ANULACION).'}
          required
        />
        {returnable ? (
          <Checkbox
            label="Anular y devolver la mercadería"
            description={`El cliente devuelve TODO lo de la venta ${current.saleNumber}: vuelve el stock, se registra el reembolso y el asiento inverso. Sin marcar, la venta sigue y puede re-emitir la factura con los datos corregidos.`}
            checked={returnGoods}
            onChange={setReturnGoods}
          />
        ) : (
          <p className="text-sm text-text-muted">Este documento no es la factura de una venta: la anulación no devuelve mercadería.</p>
        )}
        <TextArea label="Nota" value={note} onChange={setNote} maxLength={250} rows={2} optional hint="Queda en la bitácora del documento." />
        <Checkbox
          label={`Confirmo que anulo el documento N° ${current.number} ante el SIN.`}
          checked={confirmed}
          onChange={setConfirmed}
          error={touched ? errors.confirmed : undefined}
        />
      </Form>
    </Dialog>
  );
}
