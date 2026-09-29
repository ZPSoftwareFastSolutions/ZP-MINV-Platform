// Módulo «Integraciones» · crear una API Key para la tienda en línea o el ERP (`CreateApiKeyCommand`), como el editor del
// escritorio: para qué es, alcances con casillas (se marcan «leer el catálogo» y «leer el stock»), sucursal y
// vencimiento en listas desplegables. La llave actúa con los permisos de quien la crea, recortados por los alcances. El
// token completo lo devuelve el servidor UNA sola vez: la pantalla lo muestra con «Copiar» (SecretDialog).

import { KeyRound } from 'lucide-react';
import { useId, useState } from 'react';
import { useRpcCommand } from '@/4-presentation/panel/hooks';
import { Button, Checkbox, Dialog, FieldGroup, Form, FormGrid, SelectField, TextField, type SelectOption } from '@/4-presentation/panel/kit';
import { KEY_EXPIRY_OPTIONS, createKeyPayload, initialKeyForm, keyFormProblems, type CreatedKey, type KeyForm, type NamedOption } from './integrations';

export interface CreateApiKeyDialogProps {
  open: boolean;
  scopes: readonly NamedOption[];
  /** Sucursales que se pueden elegir (las visibles y activas). */
  branches: readonly SelectOption[];
  onClose: () => void;
  onCreated: (created: CreatedKey) => void;
}

export function CreateApiKeyDialog({ open, scopes, branches, onClose, onCreated }: CreateApiKeyDialogProps) {
  const formId = useId();
  const create = useRpcCommand('CreateApiKeyCommand', { notifyError: false, success: (created) => `API Key «${created.name}» creada` });
  const [form, setForm] = useState<KeyForm>(() => initialKeyForm(scopes));
  const [touched, setTouched] = useState(false);
  const [wasOpen, setWasOpen] = useState(false);
  // Cada vez que se abre, el formulario empieza de nuevo.
  if (open !== wasOpen) {
    setWasOpen(open);
    if (open) {
      setForm(initialKeyForm(scopes));
      setTouched(false);
    }
  }
  const problems = keyFormProblems(form);

  const set = <K extends keyof KeyForm>(key: K, value: KeyForm[K]) => {
    setForm((current) => ({ ...current, [key]: value }));
    if (create.error) create.reset();
  };

  const close = () => {
    create.reset();
    onClose();
  };

  const submit = async () => {
    setTouched(true);
    if (Object.keys(problems).length > 0) return;
    const outcome = await create.run(createKeyPayload(form, scopes));
    if (!outcome.ok) return;
    onCreated(outcome.result);
    close();
  };

  return (
    <Dialog
      open={open}
      onClose={close}
      dismissible={!create.sending}
      size="lg"
      title="Nueva API Key"
      description="Una llave para que otro sistema (la tienda en línea, el ERP) use el API Gateway con los permisos que usted elija."
      footer={
        <>
          <Button variant="outline" onClick={close} disabled={create.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<KeyRound />} loading={create.sending}>
            Crear la llave
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={create.errorText} busy={create.sending}>
        <TextField
          label="¿Para qué es?"
          value={form.name}
          onChange={(value) => set('name', value)}
          error={touched ? problems.name : undefined}
          hint="Un nombre para reconocerla: «Tienda en línea», «ERP contable»."
          maxLength={100}
          required
          data-autofocus
        />
        <FieldGroup label="Alcances" required error={touched ? problems.scopes : undefined} hint="Qué puede hacer la llave. Siempre con los permisos de su usuario como tope.">
          {scopes.map((scope) => (
            <Checkbox
              key={scope.code}
              label={scope.name}
              description={scope.code}
              checked={form.scopes.includes(scope.code)}
              onChange={(checked) => set('scopes', checked ? [...form.scopes, scope.code] : form.scopes.filter((item) => item !== scope.code))}
            />
          ))}
        </FieldGroup>
        <FormGrid>
          <SelectField
            label="Sucursal"
            allLabel="Todas mis sucursales"
            value={form.branch}
            onChange={(value) => set('branch', value)}
            options={branches}
            hint="Limítela a una sucursal si el sistema solo trabaja con esa."
          />
          <SelectField label="Vencimiento" allLabel="Sin vencimiento" value={form.expiry} onChange={(value) => set('expiry', value)} options={KEY_EXPIRY_OPTIONS} />
        </FormGrid>
      </Form>
    </Dialog>
  );
}
