// Módulo «Clientes» · alta y edición de un cliente en un diálogo (el editor lateral del escritorio): nombre, categoría,
// datos de factura del SIN (tipo de documento, número y complemento, con sus reglas) y la verificación del NIT en el Padrón,
// correo, teléfono y si está activo. Valida en la página (comodidad) y muestra el error del servidor DENTRO del diálogo.
//
// Se guarda con dos comandos, como el escritorio: `SaveCustomerCommand` (los datos; al crear, el servidor da el código) y
// `SaveCustomerFiscalIdentityCommand` (tipo de documento y complemento). Al EDITAR, primero los datos de factura y después
// el cliente: así un cambio de tipo (de CI a pasaporte, por ejemplo) no choca con la regla del tipo anterior. Si el cliente
// nuevo se creó pero sus datos de factura no pasaron, el diálogo recuerda su código: el reintento corrige ESE cliente y no
// crea otro.
//
// Se monta de nuevo en cada apertura (la pantalla le cambia la `key`).

import { Save, ShieldCheck } from 'lucide-react';
import { useId, useState } from 'react';
import { usePermissions, useRpcCommand } from '@/4-presentation/panel/hooks';
import { Alert, Button, Dialog, Form, FormGrid, SelectField, Switch, TextField, useNotify } from '@/4-presentation/panel/kit';
import {
  DOCUMENT_CI,
  DOCUMENT_NIT,
  DOCUMENT_TYPES,
  FINAL_CONSUMER,
  LIMITS,
  customerProblems,
  draftOf,
  hasProblems,
  plainMessage,
  toSaveCustomer,
  toSaveIdentity,
  verifiableNit,
  type CategoryRecord,
  type CustomerDraft,
  type CustomerItem,
} from './customers';

/** A quién se edita: `item` null = cliente nuevo. */
export interface CustomerTarget {
  item: CustomerItem | null;
}

export interface CustomerDialogProps {
  /** null = cerrado. */
  target: CustomerTarget | null;
  categories: readonly CategoryRecord[];
  /** La empresa factura con el SIN: se editan los datos de factura. */
  fiscalEnabled: boolean;
  onClose: () => void;
  onSaved: (code: string) => void;
}

interface NitStatus {
  tone: 'success' | 'danger' | 'warning';
  text: string;
}

export function CustomerDialog({ target, categories, fiscalEnabled, onClose, onSaved }: CustomerDialogProps) {
  const formId = useId();
  const notify = useNotify();
  const { canRun } = usePermissions();
  const [initial] = useState(target);
  const item = initial?.item ?? null;
  const [draft, setDraft] = useState<CustomerDraft>(() => draftOf(item, categories));
  const [touched, setTouched] = useState(false);
  const [createdCode, setCreatedCode] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [nit, setNit] = useState<NitStatus | null>(null);
  const save = useRpcCommand('SaveCustomerCommand', { notifyError: false });
  const saveIdentity = useRpcCommand('SaveCustomerFiscalIdentityCommand', { notifyError: false });
  const verify = useRpcCommand('VerifyNitCommand', { notifyError: false });

  const code = item?.row.code ?? createdCode;
  const errors = customerProblems(draft, code, fiscalEnabled);
  const shownError = (field: keyof typeof errors) => (touched ? errors[field] : undefined);
  const busy = save.sending || saveIdentity.sending;
  const isCi = Number(draft.documentType) === DOCUMENT_CI;
  const isNit = Number(draft.documentType) === DOCUMENT_NIT;
  const nitNumber = verifiableNit(draft);
  const canVerify = fiscalEnabled && isNit && canRun('VerifyNitCommand');

  const change = <K extends keyof CustomerDraft>(field: K, value: CustomerDraft[K]) => {
    setDraft((current) => {
      const next = { ...current, [field]: value };
      // El complemento es solo de la cédula de identidad.
      if (field === 'documentType' && Number(value) !== DOCUMENT_CI) next.complement = '';
      return next;
    });
    if (field === 'documentType' || field === 'taxId') setNit(null);
    setError(null);
  };

  const submit = async () => {
    setTouched(true);
    if (hasProblems(errors)) return;
    setError(null);
    let saved = code;
    if (saved === null) {
      // Cliente nuevo: primero el cliente (el servidor da el código) y después sus datos de factura.
      const created = await save.run(toSaveCustomer(null, draft));
      if (!created.ok) {
        setError(created.message);
        return;
      }
      saved = created.result;
      setCreatedCode(saved);
      if (fiscalEnabled) {
        const identity = await saveIdentity.run(toSaveIdentity(saved, draft));
        if (!identity.ok) {
          setError(`El cliente se creó con el código ${saved}, pero sus datos de factura no se guardaron: ${identity.message} Corríjalos y vuelva a guardar.`);
          return;
        }
      }
    } else {
      // Cliente que ya existe: primero los datos de factura y después el cliente.
      if (fiscalEnabled) {
        const identity = await saveIdentity.run(toSaveIdentity(saved, draft));
        if (!identity.ok) {
          setError(identity.message);
          return;
        }
      }
      const updated = await save.run(toSaveCustomer(saved, draft));
      if (!updated.ok) {
        setError(updated.message);
        return;
      }
    }
    notify.success(item ? 'Cliente actualizado' : 'Cliente creado', `${saved} · ${draft.name.trim()}`);
    onSaved(saved);
    onClose();
  };

  const verifyNit = async () => {
    if (nitNumber === null) return;
    const outcome = await verify.run({ nit: nitNumber, customerCode: code });
    if (!outcome.ok) {
      setNit({ tone: 'warning', text: `No se pudo verificar ahora: ${outcome.message}` });
      return;
    }
    setNit(
      outcome.result.isValid
        ? { tone: 'success', text: `NIT activo en el Padrón · ${plainMessage(outcome.result.description)}` }
        : { tone: 'danger', text: plainMessage(outcome.result.description) },
    );
  };

  return (
    <Dialog
      open={target !== null}
      onClose={onClose}
      dismissible={!busy}
      size="lg"
      title={item ? `Editar el cliente ${item.row.code}` : createdCode ? `Cliente ${createdCode}` : 'Nuevo cliente'}
      description={item ? item.row.name : 'El código lo asigna el sistema al guardar.'}
      footer={
        <>
          <Button variant="outline" onClick={onClose} disabled={busy}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<Save />} loading={busy}>
            Guardar
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={error} busy={busy}>
        <FormGrid>
          <TextField
            label="Nombre o razón social"
            value={draft.name}
            onChange={(value) => change('name', value)}
            maxLength={LIMITS.name}
            error={shownError('name')}
            className="sm:col-span-2"
            autoComplete="off"
            required
            data-autofocus
          />
          <SelectField
            label="Categoría"
            allLabel={false}
            placeholder="Elija la categoría"
            value={draft.categoryCode}
            onChange={(value) => change('categoryCode', value)}
            options={categories.map((category) => ({ value: category.code, label: category.name }))}
            error={shownError('categoryCode')}
            required
          />
          {fiscalEnabled && (
            <SelectField
              label="Tipo de documento"
              allLabel="Sin tipo de documento"
              value={draft.documentType}
              onChange={(value) => change('documentType', value)}
              options={DOCUMENT_TYPES.map((type) => ({ value: String(type.code), label: type.label }))}
              hint="Con el que se le factura (catálogo del SIN)."
              error={shownError('documentType')}
            />
          )}
          <TextField
            label={fiscalEnabled && draft.documentType ? 'Número de documento' : 'NIT / CI'}
            value={draft.taxId}
            onChange={(value) => change('taxId', value)}
            maxLength={LIMITS.taxId}
            inputMode={isCi || isNit ? 'numeric' : undefined}
            autoComplete="off"
            hint={isCi || isNit ? 'Solo números, sin puntos ni guiones.' : fiscalEnabled && draft.documentType ? undefined : 'Para la factura (opcional).'}
            error={shownError('taxId')}
            required={fiscalEnabled && draft.documentType !== ''}
            optional={!fiscalEnabled || draft.documentType === ''}
          />
          {fiscalEnabled && isCi && (
            <TextField
              label="Complemento"
              value={draft.complement}
              onChange={(value) => change('complement', value.toUpperCase())}
              maxLength={LIMITS.complement}
              autoComplete="off"
              hint="Solo si la cédula lo tiene (duplicados del SEGIP)."
              error={shownError('complement')}
              optional
            />
          )}
          {canVerify && (
            <div className="space-y-2 sm:col-span-2">
              <Button variant="subtle" leftIcon={<ShieldCheck />} disabled={nitNumber === null} loading={verify.sending} onClick={() => void verifyNit()}>
                Verificar el NIT en el Padrón
              </Button>
              {nit && (
                <Alert tone={nit.tone} className="text-sm">
                  {nit.text}
                </Alert>
              )}
            </div>
          )}
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
            error={shownError('email')}
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
            error={shownError('phone')}
            optional
          />
          <Switch
            label="Cliente activo"
            description={code === FINAL_CONSUMER ? 'El consumidor final no se puede desactivar.' : 'Un cliente inactivo no aparece para vender.'}
            checked={draft.isActive}
            disabled={code === FINAL_CONSUMER}
            onChange={(value) => change('isActive', value)}
            className="sm:col-span-2"
          />
        </FormGrid>
      </Form>
    </Dialog>
  );
}
