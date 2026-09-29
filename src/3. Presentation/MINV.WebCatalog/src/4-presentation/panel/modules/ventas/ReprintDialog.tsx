// Módulo «Ventas» · reimprimir la factura del SIN de una venta (permiso «ver la facturación»):
//   - La vista de la factura (`GetFiscalPrintModelQuery`) y «Imprimir», que imprime SOLO la factura con la impresora del
//     navegador (área imprimible, `PrintArea`).
//   - «Descargar PDF»: la representación gráfica oficial que arma el servidor (`RenderFiscalDocumentQuery`, media carta).
//     Como el escritorio, la entrega en PDF queda registrada como evidencia (`RecordFiscalDeliveryCommand`).
//
// Se monta de nuevo en cada apertura (la pantalla le cambia la `key`).

import { Download, Printer } from 'lucide-react';
import { useState } from 'react';
import { useRpcCommand, useRpcQuery } from '@/4-presentation/panel/hooks';
import { Button, Dialog, ErrorState, LoadingState, useNotify } from '@/4-presentation/panel/kit';
import { downloadServerFile } from './download';
import { InvoiceSheet } from './InvoiceSheet';
import { PrintArea } from './PrintArea';
import type { InvoiceTarget } from './SendInvoiceDialog';

export interface ReprintDialogProps {
  /** null = cerrado. */
  target: InvoiceTarget | null;
  onClose: () => void;
}

/** Columnas del rollo (solo aplica al formato de impresora térmica; el PDF las ignora). */
const ROLL_COLUMNS = 48;

export function ReprintDialog({ target, onClose }: ReprintDialogProps) {
  const notify = useNotify();
  const [shown] = useState(target);
  const current = target ?? shown;
  const open = target !== null;
  const documentId = current?.fiscal.documentId ?? '';
  const model = useRpcQuery('GetFiscalPrintModelQuery', { documentId }, { enabled: open && documentId !== '' });
  // Consulta a pedido (al pulsar «Descargar PDF»): `run` la envía una vez y devuelve el archivo.
  const pdf = useRpcCommand('RenderFiscalDocumentQuery', { errorTitle: 'No se pudo generar el PDF' });
  const delivery = useRpcCommand('RecordFiscalDeliveryCommand', { notifyError: false });

  if (!current) return null;
  const number = current.fiscal.number;

  const download = async () => {
    const outcome = await pdf.run({ documentId, format: 'Pdf', columns: ROLL_COLUMNS });
    if (!outcome.ok) return;
    const fileName = downloadServerFile(outcome.result, `factura-${number}.pdf`);
    if (!fileName) {
      notify.error('No se pudo descargar el PDF', 'El archivo llegó incompleto. Intente de nuevo.');
      return;
    }
    notify.success('PDF listo', `Se descargó ${fileName}.`);
    // Evidencia de la entrega (como el escritorio); si falla, la descarga ya se hizo.
    void delivery.run({ documentId, channel: 'Pdf', recipient: null });
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      size="lg"
      title={`Reimprimir la factura N° ${number}`}
      description={`Venta ${current.invoiceNumber} · ${current.customer}`}
      footer={
        <>
          <Button variant="outline" onClick={onClose}>
            Cerrar
          </Button>
          <Button variant="outline" leftIcon={<Download />} loading={pdf.sending} onClick={() => void download()}>
            Descargar PDF
          </Button>
          <Button leftIcon={<Printer />} disabled={!model.data} onClick={() => window.print()}>
            Imprimir
          </Button>
        </>
      }
    >
      {model.error ? (
        <ErrorState error={model.error} operation="GetFiscalPrintModelQuery" onRetry={model.reload} retrying={model.fetching} />
      ) : model.data ? (
        <>
          <InvoiceSheet model={model.data} />
          {open && (
            <PrintArea>
              <InvoiceSheet model={model.data} paper />
            </PrintArea>
          )}
        </>
      ) : (
        <LoadingState label="Cargando la factura…" rows={4} />
      )}
    </Dialog>
  );
}
