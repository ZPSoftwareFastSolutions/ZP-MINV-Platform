// Módulo «Documentos fiscales» · «Ver e imprimir» (el «Ver PDF» e «Imprimir rollo» del escritorio, en el navegador):
//   - La vista del documento (`GetFiscalPrintModelQuery`) e «Imprimir», que imprime SOLO el documento con la impresora del
//     navegador (área imprimible).
//   - «Descargar PDF»: la representación gráfica oficial que arma el servidor (`RenderFiscalDocumentQuery`).
// Como el escritorio, cada entrega queda registrada como evidencia para el SIN (`RecordFiscalDeliveryCommand`).
// La pantalla lo monta de nuevo en cada apertura (le cambia la `key`).

import { Download, Printer } from 'lucide-react';
import { useState } from 'react';
import { useRpcCommand, useRpcQuery } from '@/4-presentation/panel/hooks';
import { Button, Dialog, ErrorState, LoadingState, useNotify } from '@/4-presentation/panel/kit';
import { DocumentSheet } from './DocumentSheet';
import { documentTitle, type DocumentRecord } from './fiscal';
import { downloadServerFile } from './files';
import { PrintArea } from './PrintArea';

export interface PrintDialogProps {
  /** null = cerrado. */
  target: DocumentRecord | null;
  onClose: () => void;
  /** Se registró una entrega (para volver a leer el detalle). */
  onDelivered: () => void;
}

/** Columnas del rollo (el formato PDF las ignora; el servidor exige de 32 a 64). */
const ROLL_COLUMNS = 48;

export function PrintDialog({ target, onClose, onDelivered }: PrintDialogProps) {
  const notify = useNotify();
  const [shown] = useState(target);
  const current = target ?? shown;
  const open = target !== null;
  const documentId = current?.id ?? '';
  const model = useRpcQuery('GetFiscalPrintModelQuery', { documentId }, { enabled: open && documentId !== '' });
  // Consulta a pedido (al pulsar «Descargar PDF»): `run` la envía una vez y devuelve el archivo.
  const pdf = useRpcCommand('RenderFiscalDocumentQuery', { errorTitle: 'No se pudo generar el PDF' });
  const delivery = useRpcCommand('RecordFiscalDeliveryCommand', { notifyError: false, onSuccess: onDelivered });

  if (!current) return null;
  const title = documentTitle(current);

  const download = async () => {
    const outcome = await pdf.run({ documentId, format: 'Pdf', columns: ROLL_COLUMNS });
    if (!outcome.ok) return;
    const fileName = downloadServerFile(outcome.result, `documento-${current.number}.pdf`);
    if (!fileName) {
      notify.error('No se pudo descargar el PDF', 'El archivo llegó incompleto. Intente de nuevo.');
      return;
    }
    notify.success('PDF listo', `Se descargó ${fileName}.`);
    // Evidencia de la entrega (como el escritorio); si falla, la descarga ya se hizo.
    void delivery.run({ documentId, channel: 'Pdf', recipient: null });
  };

  const print = () => {
    window.print();
    void delivery.run({ documentId, channel: 'Print', recipient: null });
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      size="lg"
      title={`Ver e imprimir · ${title}`}
      description={`${current.buyerName} · ${current.buyerDocument}`}
      footer={
        <>
          <Button variant="outline" onClick={onClose}>
            Cerrar
          </Button>
          <Button variant="outline" leftIcon={<Download />} loading={pdf.sending} onClick={() => void download()}>
            Descargar PDF
          </Button>
          <Button leftIcon={<Printer />} disabled={!model.data} onClick={print}>
            Imprimir
          </Button>
        </>
      }
    >
      {model.error ? (
        <ErrorState error={model.error} operation="GetFiscalPrintModelQuery" onRetry={model.reload} retrying={model.fetching} />
      ) : model.data ? (
        <>
          <DocumentSheet model={model.data} />
          {open && (
            <PrintArea>
              <DocumentSheet model={model.data} paper />
            </PrintArea>
          )}
        </>
      ) : (
        <LoadingState label="Cargando el documento…" rows={4} />
      )}
    </Dialog>
  );
}
