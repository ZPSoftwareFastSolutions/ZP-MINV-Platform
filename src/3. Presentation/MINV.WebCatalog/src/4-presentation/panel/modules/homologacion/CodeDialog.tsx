// Módulo «Homologación» · elegir el código del SIN de una unidad de medida (`SaveUnitHomologationCommand`) o de un medio de
// pago (`SavePaymentMethodHomologationCommand`), con el catálogo sincronizado del SIN en una lista desplegable. El
// comando lo envía la pantalla (`onSubmit`); el error del servidor se muestra dentro del diálogo.
// La pantalla lo monta de nuevo en cada apertura (le cambia la `key`).

import { Save } from 'lucide-react';
import { useId, useState } from 'react';
import { Alert, Button, Dialog, Form, SelectField } from '@/4-presentation/panel/kit';

export interface CodeDialogTarget {
  title: string;
  description: string;
  /** Etiqueta de la lista («Unidad del SIN», «Método de pago del SIN»). */
  label: string;
  currentCode: number | null;
  options: readonly { value: string; label: string }[];
}

export interface CodeDialogProps {
  /** null = cerrado. */
  target: CodeDialogTarget | null;
  sending: boolean;
  errorText: string | null;
  onClose: () => void;
  /** Envía el comando; el diálogo se cierra si terminó bien. */
  onSubmit: (code: number) => Promise<{ ok: boolean }>;
}

export function CodeDialog({ target, sending, errorText, onClose, onSubmit }: CodeDialogProps) {
  const formId = useId();
  const [shown] = useState(target);
  const current = target ?? shown;
  const [code, setCode] = useState(() => (current?.currentCode != null ? String(current.currentCode) : ''));
  const [touched, setTouched] = useState(false);

  if (!current) return null;
  const empty = current.options.length === 0;

  const submit = async () => {
    setTouched(true);
    if (!code) return;
    const outcome = await onSubmit(Number(code));
    if (outcome.ok) onClose();
  };

  return (
    <Dialog
      open={target !== null}
      onClose={onClose}
      dismissible={!sending}
      title={current.title}
      description={current.description}
      footer={
        <>
          <Button variant="outline" onClick={onClose} disabled={sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<Save />} loading={sending} disabled={empty}>
            Guardar
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={errorText} busy={sending}>
        {empty && (
          <Alert tone="warning" title="El catálogo del SIN no está sincronizado">
            Sincronice los catálogos del SIN (Administración › Configuración › Facturación).
          </Alert>
        )}
        <SelectField
          label={current.label}
          allLabel={false}
          placeholder="Elija el código del SIN"
          value={code}
          onChange={setCode}
          options={current.options}
          error={touched && !code ? 'Elija el código del SIN.' : undefined}
          required
        />
      </Form>
    </Dialog>
  );
}
