// Módulo «Contabilidad» · diálogo «Nueva cuenta» (`CreateAccountCommand`). La cuenta se agrega como subcuenta
// IMPUTABLE del grupo elegido y hereda su tipo. El código se sugiere (la siguiente subcuenta del grupo) y se puede
// cambiar. Valida en la página (comodidad) y muestra el error del servidor dentro del diálogo, sin cerrarlo.

import { FolderPlus } from 'lucide-react';
import { useId, useMemo, useState } from 'react';
import { useRpcCommand } from '@/4-presentation/panel/hooks';
import { Button, ComboBox, Dialog, DetailList, Form, TextField, type ComboOption } from '@/4-presentation/panel/kit';
import { ACCOUNT_CODE_MAX, ACCOUNT_NAME_MAX, accountFormErrors, accountLabel, accountTypeLabel, groupAccounts, suggestCode, type AccountRecord } from './accounting';

export interface CreateAccountDialogProps {
  open: boolean;
  /** Grupo con que se abre (desde «Crear una subcuenta»); null = el de gastos de operación, si existe. */
  parentCode: string | null;
  accounts: readonly AccountRecord[];
  onClose: () => void;
  onCreated: (code: string) => void;
}

/** Grupo con que se abre si no se eligió uno: «Gastos de operación», como el escritorio. */
const DEFAULT_PARENT = '6.1';

export function CreateAccountDialog({ open, parentCode, accounts, onClose, onCreated }: CreateAccountDialogProps) {
  const formId = useId();
  const groups = useMemo(() => groupAccounts(accounts), [accounts]);
  const options = useMemo<ComboOption<AccountRecord>[]>(() => groups.map((group) => ({ value: group.code, label: accountLabel(group), description: accountTypeLabel(group.type), data: group })), [groups]);
  const initialParent = parentCode ?? (groups.some((group) => group.code === DEFAULT_PARENT) ? DEFAULT_PARENT : '');
  const [parent, setParent] = useState(initialParent);
  const [code, setCode] = useState(() => (initialParent ? suggestCode(accounts, initialParent) : ''));
  const [name, setName] = useState('');
  const [touched, setTouched] = useState(false);
  const create = useRpcCommand('CreateAccountCommand', {
    success: (result, payload) => `Cuenta ${result} · ${payload.name} creada`,
    notifyError: false,
  });

  const errors = accountFormErrors({ parentCode: parent, code, name }, accounts);
  const shown = touched ? errors : {};
  const group = groups.find((item) => item.code === parent);

  const chooseParent = (option: ComboOption<AccountRecord> | null) => {
    const next = option?.value ?? '';
    setParent(next);
    setCode(next ? suggestCode(accounts, next) : '');
    if (create.error) create.reset();
  };

  const submit = async () => {
    setTouched(true);
    if (Object.keys(errors).length > 0) return;
    const outcome = await create.run({ code: code.trim(), name: name.trim(), parentCode: parent });
    if (outcome.ok) onCreated(outcome.result);
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      dismissible={!create.sending}
      title="Nueva cuenta"
      description="Se agrega como cuenta imputable del grupo elegido y hereda su tipo."
      footer={
        <>
          <Button variant="outline" onClick={onClose} disabled={create.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<FolderPlus />} loading={create.sending}>
            Crear cuenta
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={create.errorText} busy={create.sending}>
        <ComboBox
          label="Grupo (cuenta padre)"
          placeholder="Código o nombre del grupo"
          value={options.find((option) => option.value === parent) ?? null}
          onChange={chooseParent}
          options={options}
          error={shown.parent}
          required
        />
        {group && <DetailList columns={1} items={[{ label: 'Tipo de la cuenta nueva', value: accountTypeLabel(group.type) }]} />}
        <TextField
          label="Código"
          value={code}
          onChange={(value) => {
            setCode(value);
            if (create.error) create.reset();
          }}
          error={shown.code}
          hint={group ? `Empieza con ${group.code}. y no puede repetirse.` : undefined}
          maxLength={ACCOUNT_CODE_MAX}
          autoComplete="off"
          spellCheck={false}
          inputClassName="font-mono"
          required
        />
        <TextField
          label="Nombre"
          value={name}
          onChange={(value) => {
            setName(value);
            if (create.error) create.reset();
          }}
          error={shown.name}
          placeholder="Ej.: Publicidad"
          maxLength={ACCOUNT_NAME_MAX}
          required
          data-autofocus
        />
      </Form>
    </Dialog>
  );
}
