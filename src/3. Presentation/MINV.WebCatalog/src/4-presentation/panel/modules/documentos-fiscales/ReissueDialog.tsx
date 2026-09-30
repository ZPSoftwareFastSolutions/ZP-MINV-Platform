// Módulo «Documentos fiscales» · re-emitir (`ReissueFiscalDocumentCommand`, permiso «emitir facturas»), como
// `ReissueDialog` del escritorio: documento nuevo para la misma venta (número y CUF nuevos) tras un rechazo o una anulación
// por datos del comprador erróneos. En una factura los datos del comprador se corrigen (con los del documento original a
// la vista); una nota conserva el comprador de la factura original. Después se envía ya al SIN
// (`DispatchFiscalDocumentsCommand`, si la sesión puede) y la pantalla abre el documento nuevo.
// La pantalla lo monta de nuevo en cada apertura (le cambia la `key`).

import { FilePlus } from 'lucide-react';
import { useId, useState } from 'react';
import { usePermissions, useRpcCommand, useRpcQuery } from '@/4-presentation/panel/hooks';
import { Alert, Button, Dialog, ErrorState, Form, LoadingState, useNotify } from '@/4-presentation/panel/kit';
import { BuyerFields } from './BuyerFields';
import {
  CATALOG_DOCUMENT_TYPES,
  EMPTY_BUYER,
  buyerErrors,
  buyerFromDetail,
  buyerPayload,
  documentTypeOptions,
  plainMessage,
  statusLabel,
  type BuyerDraft,
  type DocumentRecord,
} from './fiscal';

export interface ReissueDialogProps {
  /** null = cerrado. */
  target: DocumentRecord | null;
  onClose: () => void;
  /** Se emitió el documento nuevo (su id). */
  onDone: (documentId: string) => void;
}

export function ReissueDialog({ target, onClose, onDone }: ReissueDialogProps) {
  const formId = useId();
  const notify = useNotify();
  const { canRun } = usePermissions();
  const [shown] = useState(target);
  const current = target ?? shown;
  const open = target !== null;
  const documentId = current?.id ?? '';
  const detail = useRpcQuery('GetFiscalDocumentQuery', { documentId }, { enabled: open && documentId !== '' });
  const types = useRpcQuery('GetSiatCatalogQuery', { catalog: CATALOG_DOCUMENT_TYPES }, { enabled: open });
  const [buyer, setBuyer] = useState<BuyerDraft>(EMPTY_BUYER);
  const [filledFrom, setFilledFrom] = useState<string | null>(null);
  const [touched, setTouched] = useState(false);
  const reissue = useRpcCommand('ReissueFiscalDocumentCommand', { notifyError: false });
  const dispatch = useRpcCommand('DispatchFiscalDocumentsCommand', { notifyError: false });

  // Los datos del comprador empiezan con los del documento (una vez que llega el detalle).
  if (detail.data && filledFrom !== detail.data.row.id) {
    setFilledFrom(detail.data.row.id);
    setBuyer(buyerFromDetail(detail.data));
  }

  if (!current) return null;
  const isNote = current.kind === 'CreditDebitNote';
  const replacedBy = detail.data?.replacedByDocumentId ?? null;
  const errors = isNote ? {} : buyerErrors(buyer);
  const invalid = Object.values(errors).some(Boolean);
  const busy = reissue.sending || dispatch.sending;

  const submit = async () => {
    setTouched(true);
    if (invalid || replacedBy || !detail.data) return;
    const outcome = await reissue.run({ documentId: current.id, buyer: isNote ? null : buyerPayload(buyer) });
    if (!outcome.ok) return;
    const row = outcome.result;
    let message = 'Se envía al SIN automáticamente.';
    if (canRun('DispatchFiscalDocumentsCommand')) {
      const sent = await dispatch.run({ documentId: row.id, max: 1 });
      if (sent.ok) {
        const found = sent.result.documents.find((document) => document.id === row.id);
        message = found ? `Documento N° ${found.number}: ${statusLabel(found).toLocaleLowerCase('es')}.` : (sent.result.messages.map(plainMessage).find(Boolean) ?? message);
      } else {
        message = `Quedó pendiente de envío: ${sent.message}`;
      }
    }
    notify.success(`Documento N° ${row.number} emitido`, message);
    onDone(row.id);
    onClose();
  };

  let body;
  if (detail.error) body = <ErrorState error={detail.error} operation="GetFiscalDocumentQuery" onRetry={detail.reload} retrying={detail.fetching} />;
  else if (!detail.data) body = <LoadingState label="Cargando el documento…" rows={2} />;
  else
    body = (
      <Form id={formId} onSubmit={submit} error={reissue.errorText} busy={busy}>
        <p className="text-sm text-text-muted">
          {isNote
            ? 'La nota se emite otra vez con el comprador de la factura original (número y CUF nuevos).'
            : 'Se emite una factura nueva (número y CUF nuevos) con las mismas líneas y el mismo medio de pago. Corrija los datos del comprador si hace falta.'}
        </p>
        {replacedBy && (
          <Alert tone="warning" title="Este documento ya se re-emitió">
            La venta ya tiene un documento nuevo. Ábralo desde el detalle («Ver el documento que lo reemplaza»).
          </Alert>
        )}
        {!isNote && (
          <BuyerFields buyer={buyer} onChange={setBuyer} typeOptions={documentTypeOptions(types.data)} errors={touched ? errors : {}} disabled={busy || replacedBy !== null} />
        )}
      </Form>
    );

  return (
    <Dialog
      open={open}
      onClose={onClose}
      dismissible={!busy}
      size="lg"
      title={`Re-emitir ${isNote ? 'la nota' : 'la factura'} N° ${current.number}`}
      description={`${current.buyerName} · ${current.buyerDocument} · estado: ${statusLabel(current).toLocaleLowerCase('es')}`}
      footer={
        <>
          <Button variant="outline" onClick={onClose} disabled={busy}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<FilePlus />} loading={busy} disabled={!detail.data || replacedBy !== null}>
            Emitir documento nuevo
          </Button>
        </>
      }
    >
      {body}
    </Dialog>
  );
}
