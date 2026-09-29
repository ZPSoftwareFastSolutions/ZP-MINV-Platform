// Módulo «Caja» · después de cobrar: número de venta y de pedido, total y vuelto, y la factura del SIN (el documento
// DEFINITIVO después de enviarlo con `DispatchFiscalDocumentsCommand`: número, CUF, estado y qué significa), con las
// entregas: «Imprimir comprobante» (vista imprimible del navegador, con `GetFiscalPrintModelQuery` o el ticket de la
// venta), «Descargar PDF» (`RenderFiscalDocumentQuery`), «Enviar por correo» y «Siguiente venta».

import { FileDown, FileText, LoaderCircle, Mail, Printer, Receipt, ShoppingCart } from 'lucide-react';
import { useState } from 'react';
import { ROUTES } from '@/4-presentation/app/routes';
import { useRpcCommand, useRpcQuery } from '@/4-presentation/panel/hooks';
import { Alert, Button, Collapsible, DetailList, Dialog, StatusBadge, useNotify } from '@/4-presentation/panel/kit';
import { formatDateTime, formatMoney } from '@/4-presentation/panel/lib';
import type { PosCapabilities } from './capabilities';
import { downloadServerFile } from './download';
import { EmailInvoiceDialog, type EmailTarget } from './EmailInvoiceDialog';
import { FISCAL_STATUSES, fiscalExplanation, fiscalHeadline, fiscalSummary, type FiscalOutcome } from './fiscal';
import { receiptFromModel, receiptFromSale } from './receipt';
import { ReceiptPrint, ReceiptSheet } from './ReceiptSheet';
import type { BuildRowData, PosStateData, SaleResultData } from './types';

/** Una venta cobrada, con lo que pasó con su factura. */
export interface CompletedSale {
  sale: SaleResultData;
  build: BuildRowData | null;
  buyerEmail: string | null;
  fiscal: FiscalOutcome | null;
  /** Estado de la caja al cobrar (empresa, NIT y sucursal para el ticket sin factura). */
  state: PosStateData | undefined;
  cashier: string | null;
}

export interface SaleResultDialogProps {
  completed: CompletedSale | null;
  onClose: () => void;
  caps: PosCapabilities;
}

export function SaleResultDialog({ completed, onClose, caps }: SaleResultDialogProps) {
  const notify = useNotify();
  // La última venta sigue a la vista mientras el diálogo se cierra (animación de salida).
  const [shown, setShown] = useState<CompletedSale | null>(completed);
  const [printRun, setPrintRun] = useState(0);
  const [email, setEmail] = useState<EmailTarget | null>(null);
  if (completed && completed !== shown) {
    setShown(completed);
    if (completed.sale !== shown?.sale) setPrintRun(0);
  }
  const open = completed !== null;
  const sale = shown?.sale ?? null;
  const fiscal = shown?.fiscal ?? null;
  const summary = sale ? fiscalSummary(sale, fiscal) : null;
  const documentId = summary?.documentId ?? null;
  const sending = fiscal?.phase === 'sending';

  const model = useRpcQuery('GetFiscalPrintModelQuery', { documentId: documentId ?? '' }, { enabled: open && caps.printModel && documentId !== null && !sending });
  const pdf = useRpcCommand('RenderFiscalDocumentQuery', { notifyError: true, errorTitle: 'No se pudo generar el PDF' });

  if (!shown || !sale || !summary) return <Dialog open={false} onClose={onClose} title="Venta cobrada" />;

  const waitingModel = caps.printModel && documentId !== null && (sending || (model.data === undefined && model.error === null));
  const receipt = model.data && documentId !== null ? receiptFromModel(model.data) : receiptFromSale(sale, shown.state, shown.cashier);
  const rejected = summary.status === 'Rejected' || summary.status === 'PackageRejected';

  const close = () => {
    setPrintRun(0);
    onClose();
  };

  const downloadPdf = async () => {
    if (!documentId) return;
    const outcome = await pdf.run({ documentId, format: 'Pdf', columns: 48 });
    if (!outcome.ok) return;
    try {
      const name = downloadServerFile(outcome.result);
      notify.success('PDF listo', `Se descargó ${name}.`);
    } catch {
      notify.error('No se pudo descargar el PDF', 'El archivo que envió el servidor no se pudo leer. Intente de nuevo.');
    }
  };

  return (
    <>
      <Dialog
        open={open}
        onClose={close}
        size="lg"
        title={`Venta ${sale.invoiceNumber} cobrada`}
        description={`${formatDateTime(sale.issuedAt)} · ${sale.customer}`}
        footer={
          <>
            {documentId && caps.email && (
              <Button variant="outline" leftIcon={<Mail />} disabled={sending} onClick={() => setEmail({ documentId, number: summary.number, email: shown.buyerEmail })}>
                Enviar por correo
              </Button>
            )}
            {documentId && caps.pdf && (
              <Button variant="outline" leftIcon={<FileDown />} disabled={sending} loading={pdf.sending} onClick={() => void downloadPdf()}>
                Descargar PDF
              </Button>
            )}
            <Button variant="outline" leftIcon={<Printer />} loading={waitingModel} onClick={() => setPrintRun((count) => count + 1)}>
              Imprimir comprobante
            </Button>
            <Button leftIcon={<ShoppingCart />} onClick={close} data-autofocus>
              Siguiente venta
            </Button>
          </>
        }
      >
        <div className="space-y-4" data-testid="venta-cobrada">
          <Alert tone="success" title={`Total ${formatMoney(sale.total)} · ${sale.paymentMethod}`}>
            {sale.change > 0 ? (
              <span className="text-lg font-semibold" data-testid="vuelto-entregar">
                Entregue vuelto de {formatMoney(sale.change)}
              </span>
            ) : (
              'Sin vuelto.'
            )}
            {shown.build && <span className="block">Se cobró {shown.build.kind === 'Cart' ? 'la reserva' : 'el armado'} {shown.build.number}: la reserva se consumió con la venta.</span>}
          </Alert>

          <DetailList
            items={[
              { label: 'Venta', value: sale.invoiceNumber },
              { label: 'Pedido', value: sale.orderNumber },
              { label: 'Cliente', value: sale.customer },
              { label: 'Medio de pago', value: sale.paymentMethod },
              { label: 'Total', value: formatMoney(sale.total) },
              { label: 'IVA incluido', value: formatMoney(sale.tax) },
            ]}
          />
          {caps.sales && (
            <Button variant="ghost" leftIcon={<Receipt />} to={ROUTES.panelModule(`ventas?q=${encodeURIComponent(sale.invoiceNumber)}`)}>
              Ver la venta en Ventas
            </Button>
          )}

          {fiscal && (
            <section aria-label="Factura del SIN" className="space-y-2 rounded-xl border border-border p-4" data-testid="factura-sin">
              {sending ? (
                <p role="status" className="flex items-center gap-2 text-sm text-text-muted">
                  <LoaderCircle aria-hidden="true" className="size-4 animate-spin" />
                  Enviando la factura al SIN…
                </p>
              ) : (
                <>
                  <div className="flex flex-wrap items-center gap-2">
                    <Receipt aria-hidden="true" className="size-5 text-accent" />
                    <h3 className="text-base font-semibold">{fiscalHeadline(summary)}</h3>
                    {summary.status && <StatusBadge status={summary.status} statuses={FISCAL_STATUSES} />}
                  </div>
                  <p className="text-sm text-text-muted">{fiscalExplanation(summary.status)}</p>
                  <DetailList
                    columns={1}
                    items={[
                      { label: 'Código de autorización (CUF)', value: summary.cuf ? <span className="font-mono break-all">{summary.cuf}</span> : null },
                      { label: 'Comprador', value: summary.buyer },
                    ]}
                  />
                </>
              )}
              {fiscal.notice && <Alert tone="info">{fiscal.notice}</Alert>}
              {fiscal.messages.length > 0 && (
                <Alert tone={rejected ? 'danger' : 'warning'} title={rejected ? 'Mensajes del SIN' : 'Avisos del envío'}>
                  <ul className="list-disc space-y-1 pl-5">
                    {fiscal.messages.map((message) => (
                      <li key={message}>{message}</li>
                    ))}
                  </ul>
                </Alert>
              )}
              {caps.documents && summary.cuf && !sending && (
                <Button variant="ghost" leftIcon={<FileText />} to={ROUTES.panelModule(`documentos-fiscales?q=${encodeURIComponent(summary.cuf)}`)}>
                  Ver en Facturación › Documentos
                </Button>
              )}
            </section>
          )}

          <Collapsible label="Ver el comprobante" openLabel="Ocultar el comprobante" level={3} icon={<Receipt />} description="Así sale impreso.">
            <ReceiptSheet view={receipt} />
          </Collapsible>
        </div>
      </Dialog>

      {open && printRun > 0 && <ReceiptPrint key={printRun} view={receipt} onDone={() => setPrintRun(0)} />}
      <EmailInvoiceDialog target={email} onClose={() => setEmail(null)} />
    </>
  );
}
