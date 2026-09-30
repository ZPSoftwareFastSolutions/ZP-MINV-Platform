// Módulo «Series» · «Registrar series de stock» (`RegisterStockSerialsCommand`, permiso `inventory.serials.manage`):
// series de unidades que YA están en el stock de la sucursal activa sin serie (inventario inicial o antes de pasar el
// producto a «lleva serie»). El servidor las asigna a las existencias sin serie y nunca acepta más series que unidades.
// El lector de códigos funciona como teclado: cada lectura termina con Enter y queda en una línea nueva del campo.
//
// Se monta de nuevo en cada apertura (la pantalla le cambia la `key`): el formulario empieza siempre vacío.

import { ScanBarcode } from 'lucide-react';
import { useId, useState } from 'react';
import { useRpcCommand } from '@/4-presentation/panel/hooks';
import { Alert, Button, ComboBox, Dialog, Form, TextArea, TextField, useNotify, type ComboOption } from '@/4-presentation/panel/kit';
import { NOTE_MAX, REGISTER_NOTE, kindLabel, parseSerialList, plainMessage, seriesCountText, type TechProductRecord } from './serials';
import { ServerError } from './ServerError';

export interface RegisterSerialsDialogProps {
  open: boolean;
  onClose: () => void;
  /** Después de registrar (la pantalla recarga la lista). */
  onRegistered: () => void;
  /** Productos que se pueden elegir; null = la sesión no puede leer el catálogo (se escribe el SKU). */
  products: readonly ComboOption<TechProductRecord>[] | null;
  /** El catálogo todavía está cargando. */
  productsLoading?: boolean;
}

export function RegisterSerialsDialog({ open, onClose, onRegistered, products, productsLoading = false }: RegisterSerialsDialogProps) {
  const notify = useNotify();
  const formId = useId();
  const [product, setProduct] = useState<ComboOption<TechProductRecord> | null>(null);
  const [sku, setSku] = useState('');
  const [text, setText] = useState('');
  const [note, setNote] = useState(REGISTER_NOTE);
  const [touched, setTouched] = useState(false);
  const register = useRpcCommand('RegisterStockSerialsCommand', { notifyError: false });

  const kind = product?.data?.serialKind ?? 'Serial';
  const parsed = parseSerialList(text, kind);
  const chosenSku = (products ? (product?.value ?? '') : sku).trim().toUpperCase();

  const productError = touched && !chosenSku ? 'Elija el producto.' : undefined;
  const serialsError =
    touched && parsed.serials.length === 0 && parsed.problems.length === 0 ? 'Escanee, escriba o pegue al menos una serie.' : touched && parsed.problems.length > 0 ? 'Corrija las series marcadas.' : undefined;
  const noteError = note.trim().length > NOTE_MAX ? `La nota admite hasta ${NOTE_MAX} caracteres.` : undefined;

  const changed = () => {
    if (register.error) register.reset();
  };

  const submit = async () => {
    setTouched(true);
    if (!chosenSku || parsed.serials.length === 0 || parsed.problems.length > 0 || noteError) return;
    const outcome = await register.run({ sku: chosenSku, serials: parsed.serials, note: note.trim() || null });
    if (outcome.ok) {
      notify.success('Series registradas', plainMessage(outcome.result));
      onRegistered();
      onClose();
    }
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      dismissible={!register.sending}
      size="lg"
      title="Registrar series de unidades en stock"
      description="Para unidades que ya están en el stock de la sucursal activa sin serie: nunca más series que unidades sin serie."
      footer={
        <>
          <Button variant="outline" onClick={onClose} disabled={register.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<ScanBarcode />} loading={register.sending}>
            Registrar series
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} busy={register.sending} error={register.errorText && <ServerError text={register.errorText} details={register.error?.errors} />}>
        {products ? (
          <ComboBox
            label="Producto"
            placeholder={productsLoading ? 'Cargando productos…' : 'Nombre o SKU'}
            value={product}
            onChange={(option) => {
              setProduct(option);
              changed();
            }}
            options={products}
            emptyText="Ningún producto con ese nombre o SKU"
            hint={product?.data ? `Se registran como ${kindLabel(product.data.serialKind)}.` : 'Los que llevan serie o tienen stock.'}
            error={productError}
            disabled={productsLoading}
            required
          />
        ) : (
          <TextField
            label="SKU del producto"
            value={sku}
            onChange={(value) => {
              setSku(value);
              changed();
            }}
            error={productError}
            autoComplete="off"
            autoCapitalize="characters"
            spellCheck={false}
            required
          />
        )}
        <TextArea
          label="Series o IMEI"
          value={text}
          onChange={(value) => {
            setText(value);
            changed();
          }}
          rows={6}
          hint="Una por línea: el lector de códigos agrega cada una con Enter. También puede pegar una lista (separada por comas o líneas)."
          error={serialsError}
          autoComplete="off"
          spellCheck={false}
          required
          data-autofocus
        />
        <p className="text-sm text-text-muted" role="status" data-testid="series-listas">
          {parsed.serials.length === 0 ? 'Todavía no hay series.' : `${seriesCountText(parsed.serials.length)} lista${parsed.serials.length === 1 ? '' : 's'} para registrar.`}
        </p>
        {parsed.problems.length > 0 && (
          <Alert tone="warning" title="Revise estas series">
            <ul className="list-disc space-y-0.5 pl-5">
              {parsed.problems.map((problem) => (
                <li key={problem}>{problem}</li>
              ))}
            </ul>
          </Alert>
        )}
        <TextField label="Nota" value={note} onChange={setNote} maxLength={NOTE_MAX} hint="Queda en la bitácora de cada serie." error={noteError} optional />
      </Form>
    </Dialog>
  );
}
