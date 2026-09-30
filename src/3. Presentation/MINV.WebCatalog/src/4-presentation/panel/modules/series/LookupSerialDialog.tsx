// Módulo «Series» · «Consultar una serie» (el botón del tablero y de la pantalla): se escanea o escribe la serie o el IMEI
// y el servidor responde su garantía vigente (`GetWarrantyStatusQuery`): producto, estado, venta, cliente, meses y fecha
// de fin (derivada de la venta, regla T-04) y si ya tiene un caso de garantía abierto. Desde aquí se ve su trazabilidad
// completa o se abre un caso en Garantías.
//
// Se monta de nuevo en cada apertura (la pantalla le cambia la `key`): el campo empieza vacío.

import { History, ScanSearch, ShieldPlus } from 'lucide-react';
import { useId, useState } from 'react';
import { ROUTES } from '@/4-presentation/app/routes';
import { useRpcQuery } from '@/4-presentation/panel/hooks';
import { Alert, Button, DetailList, Dialog, Form, StatusBadge, TextField } from '@/4-presentation/panel/kit';
import { describePanelError, formatDate } from '@/4-presentation/panel/lib';
import { SERIAL_STATES, SERIAL_MAX, canOpenClaimFor, claimPath, monthsText, openClaimPath, type WarrantyInfo } from './serials';

export interface LookupSerialDialogProps {
  open: boolean;
  onClose: () => void;
  /** Abrir la trazabilidad de la unidad encontrada (panel lateral de la pantalla). */
  onTrace: (serial: string, sku: string) => void;
  /** La sesión puede abrir casos de garantía. */
  canOpenClaim: boolean;
}

function warrantyLine(info: WarrantyInfo): { text: string; tone: 'success' | 'danger' | 'neutral' | 'info' } {
  if (info.warrantyMonths <= 0) return { text: 'El producto no tiene garantía', tone: 'neutral' };
  if (!info.warrantyUntil) return { text: `Garantía de ${monthsText(info.warrantyMonths)} (corre desde la venta)`, tone: 'info' };
  return info.inWarranty
    ? { text: `En garantía hasta el ${formatDate(info.warrantyUntil)}`, tone: 'success' }
    : { text: `Garantía vencida el ${formatDate(info.warrantyUntil)}`, tone: 'danger' };
}

export function LookupSerialDialog({ open, onClose, onTrace, canOpenClaim }: LookupSerialDialogProps) {
  const formId = useId();
  const [text, setText] = useState('');
  const [asked, setAsked] = useState('');
  const [touched, setTouched] = useState(false);
  const lookup = useRpcQuery('GetWarrantyStatusQuery', { serial: asked, sku: null }, { enabled: open && asked !== '', keepPreviousData: false });

  const submit = () => {
    setTouched(true);
    const value = text.trim();
    if (!value) return;
    if (value === asked) lookup.reload();
    else setAsked(value);
  };

  const info = asked ? lookup.data : undefined;
  const warranty = info ? warrantyLine(info) : null;
  const fieldError = touched && !text.trim() ? 'Escanee o escriba la serie o el IMEI.' : undefined;

  return (
    <Dialog
      open={open}
      onClose={onClose}
      title="Consultar una serie"
      description="Escanee o escriba la serie o el IMEI: dónde está, a quién se vendió y su garantía."
      footer={
        <>
          <Button variant="outline" onClick={onClose}>
            Cerrar
          </Button>
          <Button type="submit" form={formId} leftIcon={<ScanSearch />} loading={lookup.fetching}>
            Consultar
          </Button>
        </>
      }
    >
      <div className="space-y-4">
        <Form id={formId} onSubmit={submit}>
          <TextField
            label="Serie o IMEI"
            value={text}
            onChange={setText}
            error={fieldError}
            hint="El lector de códigos consulta solo al leer (termina con Enter)."
            autoComplete="off"
            autoCapitalize="characters"
            spellCheck={false}
            maxLength={SERIAL_MAX}
            required
            data-autofocus
          />
        </Form>

        {asked && lookup.error && (
          <Alert tone="warning" title="No se encontró la unidad">
            {describePanelError(lookup.error, { operation: 'GetWarrantyStatusQuery' })}
          </Alert>
        )}

        {info && warranty && (
          <section aria-label={`Unidad ${info.serial}`} className="space-y-3 rounded-xl border border-border bg-surface-2/60 p-4" data-testid="resultado-consulta">
            <div className="flex flex-wrap items-start justify-between gap-2">
              <div className="min-w-0">
                <p className="font-semibold break-words text-text">{info.product}</p>
                <p className="text-xs text-text-muted">
                  {info.sku} · {info.serial}
                </p>
              </div>
              <StatusBadge status={info.status} statuses={SERIAL_STATES} />
            </div>
            <p>
              <StatusBadge tone={warranty.tone}>{warranty.text}</StatusBadge>
            </p>
            <DetailList
              items={[
                { label: 'Vendida el', value: info.soldOn ? formatDate(info.soldOn) : null },
                { label: 'Venta o factura', value: info.invoiceNumber },
                { label: 'Cliente', value: info.customer ?? (info.soldOn ? 'Cliente de otra sucursal' : null) },
                { label: 'Garantía del producto', value: info.warrantyMonths > 0 ? monthsText(info.warrantyMonths) : 'Sin garantía' },
                { label: 'Caso de garantía abierto', value: info.openClaim },
              ]}
            />
            <div className="flex flex-col gap-2 sm:flex-row sm:flex-wrap">
              <Button variant="outline" leftIcon={<History />} onClick={() => onTrace(info.serial, info.sku)}>
                Ver la trazabilidad
              </Button>
              {info.openClaim ? (
                <Button variant="outline" leftIcon={<ShieldPlus />} to={ROUTES.panelModule(claimPath(info.openClaim))}>
                  Ver el caso {info.openClaim}
                </Button>
              ) : (
                canOpenClaim &&
                canOpenClaimFor(info.status) && (
                  <Button leftIcon={<ShieldPlus />} to={ROUTES.panelModule(openClaimPath(info.serial, info.sku))}>
                    Abrir caso de garantía
                  </Button>
                )
              )}
            </div>
          </section>
        )}
      </div>
    </Dialog>
  );
}
