// Módulo «Proveedores» · alta y edición de un proveedor en un diálogo (el editor del escritorio): razón social, NIT,
// días de entrega (lista desplegable con los plazos habituales y «Otro plazo…» para escribir cualquiera de 0 a 365),
// contacto, teléfono, correo y si está activo (`SaveSupplierCommand`; al crear, el servidor le da el código P001…).
// Valida en la página (comodidad) y muestra el error del servidor DENTRO del diálogo. Se monta de nuevo en cada apertura.

import { Save } from 'lucide-react';
import { useId, useState } from 'react';
import { useRpcCommand } from '@/4-presentation/panel/hooks';
import { Button, Dialog, Form, FormGrid, NumberField, SelectField, Switch, TextField, useNotify } from '@/4-presentation/panel/kit';
import {
  LEAD_TIME_CHOICES,
  LIMITS,
  MAX_LEAD_DAYS,
  draftOf,
  leadChoiceOptions,
  plainMessage,
  supplierProblems,
  toSavePayload,
  type SupplierDraft,
  type SupplierRecord,
} from './suppliers';

/** A quién se edita: `row` null = proveedor nuevo. */
export interface SupplierTarget {
  row: SupplierRecord | null;
}

export interface SupplierDialogProps {
  /** null = cerrado. */
  target: SupplierTarget | null;
  onClose: () => void;
  onSaved: (code: string) => void;
}

export function SupplierDialog({ target, onClose, onSaved }: SupplierDialogProps) {
  const formId = useId();
  const notify = useNotify();
  const [initial] = useState(target);
  const row = initial?.row ?? null;
  const [draft, setDraft] = useState<SupplierDraft>(() => draftOf(row));
  const [customLead, setCustomLead] = useState(() => !LEAD_TIME_CHOICES.includes(draft.leadTimeDays ?? 0));
  const [touched, setTouched] = useState(false);
  const save = useRpcCommand('SaveSupplierCommand', { notifyError: false });

  const problems = supplierProblems(draft);
  const shown = (field: keyof SupplierDraft) => (touched ? problems[field] : undefined);

  const change = <K extends keyof SupplierDraft>(field: K, value: SupplierDraft[K]) => {
    setDraft((current) => ({ ...current, [field]: value }));
    if (save.error) save.reset();
  };

  const close = () => {
    save.reset();
    onClose();
  };

  const submit = async () => {
    setTouched(true);
    if (Object.keys(problems).length > 0) return;
    const outcome = await save.run(toSavePayload(row?.code ?? null, draft));
    if (outcome.ok) {
      const code = plainMessage(outcome.result) || row?.code || '';
      notify.success(row ? 'Proveedor actualizado' : 'Proveedor creado', `${code} · ${draft.name.trim()}`);
      onSaved(code);
      close();
    }
  };

  return (
    <Dialog
      open={target !== null}
      onClose={close}
      dismissible={!save.sending}
      size="lg"
      title={row ? `Editar el proveedor ${row.code}` : 'Nuevo proveedor'}
      description={row ? row.name : 'El código lo asigna el sistema al guardar.'}
      footer={
        <>
          <Button variant="outline" onClick={close} disabled={save.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<Save />} loading={save.sending}>
            Guardar
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={save.errorText} busy={save.sending}>
        <FormGrid>
          <TextField
            label="Razón social"
            value={draft.name}
            onChange={(value) => change('name', value)}
            maxLength={LIMITS.name}
            autoComplete="off"
            error={shown('name')}
            className="sm:col-span-2"
            required
            data-autofocus
          />
          <TextField
            label="NIT"
            value={draft.taxId}
            onChange={(value) => change('taxId', value)}
            maxLength={LIMITS.taxId}
            inputMode="numeric"
            autoComplete="off"
            error={shown('taxId')}
            optional
          />
          <SelectField
            label="Días de entrega"
            allLabel={false}
            value={customLead ? 'otro' : String(draft.leadTimeDays ?? '')}
            onChange={(value) => {
              if (value === 'otro') {
                setCustomLead(true);
                return;
              }
              setCustomLead(false);
              change('leadTimeDays', Number(value));
            }}
            options={leadChoiceOptions()}
            hint="Lo usa el pedido sugerido para calcular cuándo llega lo que se pide."
            error={customLead ? undefined : shown('leadTimeDays')}
            required
          />
          {customLead && (
            <NumberField
              label="Plazo de entrega en días"
              value={draft.leadTimeDays}
              onChange={(value) => change('leadTimeDays', value)}
              unit="días"
              hint={`De 0 a ${MAX_LEAD_DAYS}.`}
              error={shown('leadTimeDays')}
              required
            />
          )}
          <TextField
            label="Contacto"
            value={draft.contactName}
            onChange={(value) => change('contactName', value)}
            maxLength={LIMITS.contact}
            autoComplete="off"
            error={shown('contactName')}
            optional
          />
          <TextField
            label="Teléfono"
            type="tel"
            inputMode="tel"
            value={draft.phone}
            onChange={(value) => change('phone', value)}
            maxLength={LIMITS.phone}
            autoComplete="off"
            error={shown('phone')}
            optional
          />
          <TextField
            label="Correo"
            type="email"
            inputMode="email"
            value={draft.email}
            onChange={(value) => change('email', value)}
            maxLength={LIMITS.email}
            autoComplete="off"
            autoCapitalize="none"
            spellCheck={false}
            error={shown('email')}
            optional
          />
          <Switch
            label="Proveedor activo"
            description="Un proveedor inactivo no recibe órdenes de compra nuevas."
            checked={draft.isActive}
            onChange={(value) => change('isActive', value)}
            className="sm:col-span-2"
          />
        </FormGrid>
      </Form>
    </Dialog>
  );
}
