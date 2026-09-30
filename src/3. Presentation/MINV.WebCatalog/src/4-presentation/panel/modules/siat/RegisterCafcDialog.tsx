// Módulo «Estado del SIAT» · registrar un talonario de facturas de contingencia (`RegisterContingencyCodeCommand`, permiso
// «gestionar eventos significativos, paquetes y CAFC»), como `RegisterCafcDialog` del escritorio: sucursal (las que tienen
// código del Padrón), documento, código CAFC que entregó el SIN, rango de números y vencimiento.
// La pantalla lo monta de nuevo en cada apertura (le cambia la `key`).

import { Plus } from 'lucide-react';
import { useId, useState } from 'react';
import { usePermissions, useRpcCommand, useRpcQuery } from '@/4-presentation/panel/hooks';
import { Alert, Button, Dialog, Form, FormGrid, NumberField, SelectField, TextField, useNotify } from '@/4-presentation/panel/kit';
import { SECTOR_OPTIONS, SECTOR_PURCHASE_SALE, plainMessage } from './siat';

export interface RegisterCafcDialogProps {
  open: boolean;
  onClose: () => void;
  onDone: () => void;
}

export function RegisterCafcDialog({ open, onClose, onDone }: RegisterCafcDialogProps) {
  const formId = useId();
  const notify = useNotify();
  const { session } = usePermissions();
  const settings = useRpcQuery('GetSiatSettingsQuery', {}, { enabled: open });
  const [branch, setBranch] = useState('');
  const [sector, setSector] = useState(String(SECTOR_PURCHASE_SALE));
  const [code, setCode] = useState('');
  const [from, setFrom] = useState<number | null>(1);
  const [to, setTo] = useState<number | null>(100);
  const [validUntil, setValidUntil] = useState('');
  const [touched, setTouched] = useState(false);
  const register = useRpcCommand('RegisterContingencyCodeCommand', { notifyError: false });

  const branches = (settings.data?.branches ?? []).filter((item) => item.siatCode !== null);
  const options = branches.map((item) => ({ value: item.branchCode, label: `${item.branchCode} · ${item.branchName} (Padrón ${item.siatCode})` }));
  const active = branches.find((item) => item.branchId === session?.access.activeBranchId);
  const chosenBranch = branch || active?.branchCode || branches[0]?.branchCode || '';
  const noBranches = settings.data !== undefined && branches.length === 0;
  const errors = {
    branch: chosenBranch ? undefined : 'Elija la sucursal.',
    code: code.trim().length > 0 ? undefined : 'Escriba el código CAFC que entregó el SIN.',
    from: from !== null && from >= 1 ? undefined : 'Escriba el primer número del talonario.',
    to: to !== null && from !== null && to >= from ? undefined : 'El último número no puede ser menor que el primero.',
  };

  const submit = async () => {
    setTouched(true);
    if (Object.values(errors).some(Boolean) || from === null || to === null) return;
    const outcome = await register.run({
      branchCode: chosenBranch,
      documentSector: Number(sector),
      code: code.trim(),
      numberFrom: from,
      numberTo: to,
      validUntil: validUntil || null,
    });
    if (!outcome.ok) return;
    notify.success('Talonario CAFC registrado', plainMessage(outcome.result));
    onDone();
    onClose();
  };

  return (
    <Dialog
      open={open}
      onClose={onClose}
      dismissible={!register.sending}
      title="Registrar talonario CAFC"
      description="El CAFC (código de autorización de facturas de contingencia) lo entrega el SIN para facturar a mano cuando se corta la energía o falla el equipo."
      footer={
        <>
          <Button variant="outline" onClick={onClose} disabled={register.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<Plus />} loading={register.sending} disabled={noBranches}>
            Registrar talonario
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={register.errorText} busy={register.sending}>
        {noBranches && (
          <Alert tone="danger" title="Ninguna sucursal tiene código del Padrón">
            Asigne el código del Padrón a la sucursal (Administración › Configuración › Facturación).
          </Alert>
        )}
        <FormGrid>
          <SelectField
            label="Sucursal"
            allLabel={false}
            placeholder={settings.data ? 'Elija la sucursal' : 'Cargando las sucursales…'}
            value={chosenBranch}
            onChange={setBranch}
            options={options}
            error={touched ? errors.branch : undefined}
            required
          />
          <SelectField label="Documento" allLabel={false} value={sector} onChange={setSector} options={SECTOR_OPTIONS} required />
          <TextField
            label="Código CAFC"
            value={code}
            onChange={setCode}
            error={touched ? errors.code : undefined}
            autoComplete="off"
            maxLength={50}
            className="sm:col-span-2"
            required
            data-autofocus
          />
          <NumberField label="Desde el número" value={from} onChange={setFrom} error={touched ? errors.from : undefined} required />
          <NumberField label="Hasta el número" value={to} onChange={setTo} error={touched ? errors.to : undefined} required />
          <TextField label="Vence" type="date" value={validUntil} onChange={setValidUntil} optional hint="Déjelo vacío si el talonario no tiene vencimiento." />
        </FormGrid>
      </Form>
    </Dialog>
  );
}
