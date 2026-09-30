// Módulo «Toma física» · confirmación de «Generar ajustes» con el RESUMEN DE DIFERENCIAS (como el escritorio): productos
// contados, sobrantes → AJUSTE (+), faltantes → AJUSTE (−), los que cuadran y las diferencias más grandes. Envía
// `PostPhysicalCountCommand` con la confirmación explícita; el servidor compara contra el stock EXACTO de ese momento y
// registra todo en una sola operación. Un error del servidor (por ejemplo, un producto con serie con diferencia) se
// muestra aquí, sin cerrar.

import { ClipboardCheck } from 'lucide-react';
import { useRpcCommand } from '@/4-presentation/panel/hooks';
import { Alert, Button, DetailList, Dialog, StatusBadge } from '@/4-presentation/panel/kit';
import { formatNumber, formatQuantity } from '@/4-presentation/panel/lib';
import { DIFFERENCES, countSummary, differenceText, kindOf, type CountSheetRecord, type PostResultRecord } from './count';

/** Diferencias que se listan en la confirmación (las más grandes). */
const SHOWN_DIFFERENCES = 10;

export interface PostCountDialogProps {
  /** La toma a contabilizar (null = cerrado). */
  sheet: CountSheetRecord | null;
  onClose: () => void;
  onPosted: (result: PostResultRecord) => void;
}

export function PostCountDialog({ sheet, onClose, onPosted }: PostCountDialogProps) {
  const post = useRpcCommand('PostPhysicalCountCommand', { notifyError: false });
  const summary = countSummary(sheet);

  const close = () => {
    post.reset();
    onClose();
  };
  const confirm = async () => {
    if (!sheet) return;
    const outcome = await post.run({ physicalCountId: sheet.id, confirmed: true });
    if (!outcome.ok) return;
    onPosted(outcome.result);
    close();
  };

  return (
    <Dialog
      open={sheet !== null}
      onClose={close}
      dismissible={!post.sending}
      alert
      title={sheet ? `¿Generar los ajustes de la toma ${sheet.number}?` : 'Generar ajustes'}
      description="Se comparan los conteos con el stock exacto de este momento y se registran los ajustes en una sola operación. Los movimientos no se borran: un error se corrige con otro ajuste."
      footer={
        <>
          <Button variant="outline" data-autofocus disabled={post.sending} onClick={close}>
            Revisar primero
          </Button>
          <Button leftIcon={<ClipboardCheck />} loading={post.sending} onClick={() => void confirm()}>
            Generar ajustes
          </Button>
        </>
      }
    >
      <div className="space-y-4" data-testid="resumen-ajustes">
        <DetailList
          items={[
            { label: 'Conteos registrados', value: `${formatNumber(summary.lines)} (${formatNumber(summary.products)} productos)` },
            { label: 'Sobrantes → AJUSTE (+)', value: formatNumber(summary.surpluses) },
            { label: 'Faltantes → AJUSTE (−)', value: formatNumber(summary.shortages) },
            { label: 'Cuadran (sin movimiento)', value: formatNumber(summary.matching) },
          ]}
        />
        {summary.differences.length > 0 && (
          <section className="space-y-2">
            <h3 className="text-sm font-semibold text-text">Diferencias más grandes</h3>
            <ul className="divide-y divide-border rounded-xl border border-border text-sm">
              {summary.differences.slice(0, SHOWN_DIFFERENCES).map((line) => (
                <li key={`${line.sku}|${line.binCode}|${line.lotNumber}`} className="flex flex-wrap items-center justify-between gap-2 px-3 py-2">
                  <span className="min-w-0">
                    <span className="block text-text">{line.name}</span>
                    <span className="block text-xs text-text-muted">
                      {line.sku} · {line.binCode} · sistema {formatQuantity(line.systemQuantity, { unit: line.unit })} · contado {formatQuantity(line.countedQuantity, { unit: line.unit })}
                    </span>
                  </span>
                  <StatusBadge tone={DIFFERENCES[kindOf(line.difference)].tone}>{differenceText(line.difference, line.unit)}</StatusBadge>
                </li>
              ))}
            </ul>
            {summary.differences.length > SHOWN_DIFFERENCES && (
              <p className="text-xs text-text-muted">Y {formatNumber(summary.differences.length - SHOWN_DIFFERENCES)} diferencias más (todas en la lista de conteos).</p>
            )}
          </section>
        )}
        <Alert tone="info">
          Un producto sin movimientos recibe su SALDO INICIAL. Los productos con serie o IMEI no se ajustan por cantidad: si alguno tiene diferencia, el servidor
          lo rechaza y la diferencia se registra en «Movimientos» indicando las series.
        </Alert>
        {post.errorText && <Alert tone="danger">{post.errorText}</Alert>}
      </div>
    </Dialog>
  );
}
