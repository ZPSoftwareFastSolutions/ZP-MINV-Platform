// Módulo «Ventas» · anular una venta (`VoidSaleCommand`), como el escritorio: motivo (uno sugerido o escrito) y
// confirmación, porque no se puede deshacer (el stock vuelve con una devolución de cliente y se registra el asiento
// inverso). Una venta con factura del SIN ACTIVA no se anula aquí: se anula ante el SIN desde Facturación › Documentos
// (con devolución de la mercadería); el diálogo lo explica y lleva allí a quien puede hacerlo. El servidor decide igual.
//
// Se monta de nuevo en cada apertura (la pantalla le cambia la `key`): el formulario empieza siempre vacío.

import { FileText } from 'lucide-react';
import { useState } from 'react';
import { permissionName } from '@/4-presentation/app/contract';
import { ROUTES } from '@/4-presentation/app/routes';
import { usePermissions, useRpcCommand } from '@/4-presentation/panel/hooks';
import { Alert, Button, ConfirmDialog, Dialog, SelectField, TextField, useNotify } from '@/4-presentation/panel/kit';
import { formatMoney } from '@/4-presentation/panel/lib';
import { OTHER_REASON, REASON_MAX, VOID_REASONS, plainMessage, reasonOptions, reasonProblem, reasonText, voidsBeforeSin, type SaleItem } from './sales';

export interface VoidSaleDialogProps {
  /** La venta a anular (null = cerrado). */
  target: SaleItem | null;
  onClose: () => void;
  /** Después de anular (la pantalla recarga la lista). */
  onVoided: (invoiceNumber: string) => void;
}

export function VoidSaleDialog({ target, onClose, onVoided }: VoidSaleDialogProps) {
  const notify = useNotify();
  const { can } = usePermissions();
  // La venta sigue a la vista mientras el diálogo se cierra.
  const [shown] = useState(target);
  const [choice, setChoice] = useState('');
  const [other, setOther] = useState('');
  const [touched, setTouched] = useState(false);
  const voidSale = useRpcCommand('VoidSaleCommand', { notifyError: false });

  const sale = target ?? shown;
  if (!sale) return null;
  const open = target !== null;

  if (voidsBeforeSin(sale.fiscal)) {
    const canVoidFiscal = can('billing.void') && can('billing.view');
    return (
      <Dialog
        open={open}
        onClose={onClose}
        title={`Anular la venta ${sale.key}`}
        description={`${sale.row.customer} · ${formatMoney(sale.row.total)}`}
        footer={
          <>
            <Button variant="outline" onClick={onClose}>
              Cerrar
            </Button>
            {canVoidFiscal && (
              <Button leftIcon={<FileText />} to={ROUTES.panelModule('documentos')}>
                Ir a Documentos fiscales
              </Button>
            )}
          </>
        }
      >
        <Alert tone="warning" title="Esta venta tiene factura del SIN">
          <p>
            La factura N° {sale.fiscal?.number} de la venta {sale.key} se anula ante el SIN desde Facturación › Documentos, con la opción de devolver la
            mercadería (vuelve el stock y se registra el reembolso).
          </p>
          <p className="mt-2">
            {canVoidFiscal
              ? `Busque allí la factura N° ${sale.fiscal?.number}.`
              : `Pídalo a quien tenga el permiso «${permissionName('billing.void')}».`}
          </p>
        </Alert>
      </Dialog>
    );
  }

  const reason = reasonText(choice, other);
  const problem = reasonProblem(reason);
  const choiceError = touched && choice === '' ? 'Elija el motivo o escriba uno.' : undefined;
  const otherError = touched && choice === OTHER_REASON && problem ? problem : undefined;

  const confirm = async () => {
    setTouched(true);
    if (problem) return false;
    const outcome = await voidSale.run({ invoiceNumber: sale.key, reason });
    if (outcome.ok) {
      notify.success('Venta anulada', plainMessage(outcome.result));
      onVoided(sale.key);
    }
    return outcome;
  };

  return (
    <ConfirmDialog
      open={open}
      onClose={onClose}
      tone="danger"
      title={`¿Anular la venta ${sale.key}?`}
      message={`Total ${formatMoney(sale.row.total)} de ${sale.row.customer}. El stock vuelve con una devolución de cliente y se registra el asiento inverso. No se puede deshacer.`}
      confirmLabel="Anular venta"
      onConfirm={confirm}
      error={voidSale.errorText}
    >
      <SelectField
        label="Motivo de la anulación"
        allLabel={false}
        placeholder="Elija el motivo"
        value={choice}
        onChange={(value) => {
          setChoice(value);
          voidSale.reset();
        }}
        options={reasonOptions(VOID_REASONS)}
        error={choiceError}
        required
      />
      {choice === OTHER_REASON && (
        <TextField
          label="Escriba el motivo"
          value={other}
          onChange={(value) => {
            setOther(value);
            voidSale.reset();
          }}
          maxLength={REASON_MAX}
          hint="Queda en la bitácora de la venta."
          error={otherError}
          required
        />
      )}
    </ConfirmDialog>
  );
}
