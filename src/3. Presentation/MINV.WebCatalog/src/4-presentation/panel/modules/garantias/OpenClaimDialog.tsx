// Módulo «Garantías» · «Abrir un caso de garantía» (`OpenWarrantyClaimCommand`, permiso `service.rma.open`), como el
// escritorio: se escanea o escribe la serie y el servidor muestra al instante el producto, la venta, el cliente y la
// garantía derivada (`GetWarrantyStatusQuery`, regla T-04). Fuera de garantía el caso solo se abre como «reparación con
// cargo», marcada explícitamente (regla T-05); si la venta es de otra sucursal se indica el cliente (`GetCustomersQuery`).
// El equipo queda «en garantía (RMA)» en la sucursal activa, sin entrar al stock vendible.
//
// Llega con la serie ya escrita desde Series (`?abrir=1&serie=…&sku=…`). Se monta de nuevo en cada apertura (la pantalla
// le cambia la `key`): el formulario empieza vacío.

import { ScanSearch, ShieldPlus } from 'lucide-react';
import { useId, useState } from 'react';
import { usePermissions, useRpcCommand, useRpcQuery } from '@/4-presentation/panel/hooks';
import { Alert, Button, Checkbox, ComboBox, DetailList, Dialog, Form, StatusBadge, TextArea, TextField, useNotify, type ComboOption } from '@/4-presentation/panel/kit';
import { describePanelError, formatDate } from '@/4-presentation/panel/lib';
import {
  ISSUE_SUGGESTIONS,
  TEXT_MAX,
  UNIT_STATES,
  customerOptions,
  issueProblem,
  lookupWarning,
  monthsText,
  needsCustomer,
  warrantyLine,
  type ClaimRecord,
  type CustomerRecord,
} from './claims';
import { ServerError } from './ServerError';

/** Serie (y producto) con que se abre el diálogo. */
export interface OpenClaimTarget {
  serial: string;
  sku: string | null;
}

export interface OpenClaimDialogProps {
  /** null = cerrado. */
  target: OpenClaimTarget | null;
  onClose: () => void;
  /** Después de abrir el caso (la pantalla recarga la lista y abre su detalle). */
  onOpened: (row: ClaimRecord) => void;
}

export function OpenClaimDialog({ target, onClose, onOpened }: OpenClaimDialogProps) {
  const notify = useNotify();
  const { canRun } = usePermissions();
  const formId = useId();
  const [serial, setSerial] = useState(target?.serial ?? '');
  // La serie consultada (y su SKU, si llegó con el pedido): la consulta se hace al salir del campo, con Enter o al abrir.
  const [asked, setAsked] = useState<OpenClaimTarget | null>(target?.serial ? { serial: target.serial.trim(), sku: target.sku } : null);
  const [issue, setIssue] = useState('');
  const [chargeable, setChargeable] = useState(false);
  const [customer, setCustomer] = useState<ComboOption<CustomerRecord> | null>(null);
  const [customerCode, setCustomerCode] = useState('');
  const [touched, setTouched] = useState(false);
  const [pendingLookup, setPendingLookup] = useState(false);
  const openClaim = useRpcCommand('OpenWarrantyClaimCommand', { notifyError: false });

  const open = target !== null;
  const lookup = useRpcQuery('GetWarrantyStatusQuery', { serial: asked?.serial ?? '', sku: asked?.sku ?? null }, { enabled: open && asked !== null, keepPreviousData: false });
  // La consulta vale para lo que está escrito ahora (si se cambió la serie, hay que volver a consultar).
  const current = asked !== null && asked.serial === serial.trim() ? lookup : null;
  const info = current?.data;
  const wantsCustomer = info ? needsCustomer(info) : false;
  const canListCustomers = canRun('GetCustomersQuery');
  const customers = useRpcQuery('GetCustomersQuery', {}, { enabled: open && wantsCustomer && canListCustomers });
  const customerChoices = customers.data ? customerOptions(customers.data.customers) : [];

  /** Consulta la serie escrita (con el SKU del pedido si es la misma serie); `again` vuelve a consultar la misma. */
  const runLookup = (again = true) => {
    const value = serial.trim();
    if (!value) return;
    if (asked?.serial === value) {
      if (again) lookup.reload();
      return;
    }
    setAsked({ serial: value, sku: target && target.serial.trim() === value ? target.sku : null });
  };

  const warranty = info ? warrantyLine(info) : null;
  const warning = info ? lookupWarning(info) : null;
  const outOfWarranty = info ? !info.inWarranty : false;
  const chosenCustomer = canListCustomers && !customers.error ? (customer?.value ?? '') : customerCode.trim().toUpperCase();

  const serialError = touched && !serial.trim() ? 'Escanee o escriba la serie o el IMEI del equipo.' : undefined;
  const problem = issueProblem(issue);
  const issueError = touched && problem ? problem : undefined;
  const chargeableError = touched && outOfWarranty && !chargeable ? 'La unidad está fuera de garantía: marque «Reparación con cargo» para abrir el caso.' : undefined;
  const customerError = touched && wantsCustomer && !chosenCustomer ? 'Indique el cliente del caso (la venta es de otra sucursal o no está a la vista).' : undefined;

  const reset = () => {
    if (openClaim.error) openClaim.reset();
  };

  const submit = async () => {
    setTouched(true);
    setPendingLookup(false);
    const value = serial.trim();
    if (!value || problem) return;
    // Sin la consulta de la serie escrita, primero se consulta (así se ve su garantía antes de abrir el caso).
    if (!info) {
      setPendingLookup(true);
      runLookup(false);
      return;
    }
    if ((outOfWarranty && !chargeable) || (wantsCustomer && !chosenCustomer)) return;
    const outcome = await openClaim.run({
      serial: value,
      issue: issue.trim(),
      chargeableRepair: outOfWarranty && chargeable,
      customerCode: wantsCustomer ? chosenCustomer : null,
      sku: info.sku,
    });
    if (outcome.ok) {
      const row = outcome.result;
      notify.success(`Caso ${row.number} abierto`, `${row.product} · ${row.serial} · ${row.isInWarranty ? 'en garantía' : 'reparación con cargo'}`);
      onOpened(row);
      onClose();
    }
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      dismissible={!openClaim.sending}
      size="lg"
      title="Abrir un caso de garantía"
      description="El equipo queda «en garantía (RMA)» en la sucursal activa, sin entrar al stock vendible."
      footer={
        <>
          <Button variant="outline" onClick={onClose} disabled={openClaim.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<ShieldPlus />} loading={openClaim.sending}>
            Abrir caso
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} busy={openClaim.sending} error={openClaim.errorText && <ServerError text={openClaim.errorText} details={openClaim.error?.errors} />}>
        <div className="flex flex-col gap-2 sm:flex-row sm:items-start">
          <TextField
            label="Serie o IMEI del equipo"
            className="min-w-0 flex-1"
            value={serial}
            onChange={(value) => {
              setSerial(value);
              reset();
            }}
            onBlur={() => runLookup(false)}
            onKeyDown={(event) => {
              if (event.key !== 'Enter') return;
              event.preventDefault();
              runLookup(false);
            }}
            error={serialError}
            hint="El lector de códigos consulta solo al leer (termina con Enter)."
            autoComplete="off"
            autoCapitalize="characters"
            spellCheck={false}
            maxLength={80}
            required
            data-autofocus
          />
          <Button variant="outline" leftIcon={<ScanSearch />} className="sm:mt-7" loading={current?.fetching ?? false} onClick={() => runLookup()}>
            Consultar
          </Button>
        </div>

        {current?.error && (
          <Alert tone="warning" title="No se encontró la unidad">
            {describePanelError(current.error, { operation: 'GetWarrantyStatusQuery' })}
          </Alert>
        )}
        {pendingLookup && !info && !current?.error && (
          <p className="text-sm text-text-muted" role="status">
            Consultando la unidad: revise su garantía y vuelva a pulsar «Abrir caso».
          </p>
        )}

        {info && warranty && (
          <section aria-label={`Unidad ${info.serial}`} className="space-y-3 rounded-xl border border-border bg-surface-2/60 p-4" data-testid="unidad-del-caso">
            <div className="flex flex-wrap items-start justify-between gap-2">
              <div className="min-w-0">
                <p className="font-semibold break-words text-text">{info.product}</p>
                <p className="text-xs text-text-muted">
                  {info.sku} · {info.serial}
                </p>
              </div>
              <StatusBadge status={info.status} statuses={UNIT_STATES} />
            </div>
            <p>
              <StatusBadge tone={warranty.tone}>{warranty.text}</StatusBadge>
            </p>
            <DetailList
              items={[
                { label: 'Vendido el', value: info.soldOn ? formatDate(info.soldOn) : null },
                { label: 'Venta o factura', value: info.invoiceNumber },
                { label: 'Cliente', value: info.customer ?? 'Hay que indicarlo (venta de otra sucursal)' },
                { label: 'Garantía del producto', value: info.warrantyMonths > 0 ? monthsText(info.warrantyMonths) : 'Sin garantía' },
              ]}
            />
            {warning && (
              <Alert tone="warning" title="Revise antes de abrir el caso">
                {warning}
              </Alert>
            )}
          </section>
        )}

        {info && outOfWarranty && (
          <Checkbox
            label="Reparación con cargo"
            description="La unidad está fuera de garantía: el caso se abre como servicio con cargo y queda marcado así."
            checked={chargeable}
            onChange={(value) => {
              setChargeable(value);
              reset();
            }}
            error={chargeableError}
          />
        )}

        {wantsCustomer &&
          (canListCustomers && !customers.error ? (
            <ComboBox
              label="Cliente del caso"
              placeholder={customers.data ? 'Nombre, código o NIT/CI' : 'Cargando clientes…'}
              value={customer}
              onChange={(option) => {
                setCustomer(option);
                reset();
              }}
              options={customerChoices}
              emptyText="Ningún cliente con ese nombre, código o NIT/CI"
              hint="La venta de la serie no está a la vista (otra sucursal): indique a quién se le recibe el equipo."
              error={customerError}
              disabled={!customers.data}
              required
            />
          ) : (
            <TextField
              label="Código del cliente del caso"
              value={customerCode}
              onChange={(value) => {
                setCustomerCode(value);
                reset();
              }}
              hint="La venta de la serie no está a la vista (otra sucursal): escriba el código del cliente."
              error={customerError}
              autoComplete="off"
              autoCapitalize="characters"
              required
            />
          ))}

        <TextArea
          label="Falla reportada"
          value={issue}
          onChange={(value) => {
            setIssue(value);
            reset();
          }}
          maxLength={TEXT_MAX}
          rows={3}
          hint="Lo que el cliente cuenta: queda en el caso y en la orden de servicio."
          error={issueError}
          required
        />
        <div className="space-y-2">
          <p className="text-sm text-text-muted">Fallas frecuentes:</p>
          <div className="flex flex-wrap gap-2">
            {ISSUE_SUGGESTIONS.map((suggestion) => (
              <Button
                key={suggestion}
                variant="subtle"
                onClick={() => {
                  setIssue(suggestion);
                  reset();
                }}
              >
                {suggestion}
              </Button>
            ))}
          </div>
        </div>
      </Form>
    </Dialog>
  );
}
