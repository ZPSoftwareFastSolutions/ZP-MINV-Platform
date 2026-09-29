// Módulo «Configuración» · diálogos de los puntos de venta del SIN, como el escritorio (RegisterPointDialog y «Vincular
// caja»): registrar un punto en el SIN (`RegisterSiatPointOfSaleCommand`: el SIN asigna su número y M-INV pide su CUIS y
// su CUFD) y vincular una caja de M-INV a un punto (`LinkPointOfSaleRegisterCommand`). Las cajas salen de la lista de la
// empresa si la sesión puede leerla (`GetPosStateQuery`); si no, se escribe su código.

import { Link2, Plus } from 'lucide-react';
import { useId, useState } from 'react';
import { useRpcCommand } from '@/4-presentation/panel/hooks';
import { Alert, Button, Dialog, Form, FormGrid, NumberField, SelectField, TextField } from '@/4-presentation/panel/kit';
import {
  DEFAULT_POINT_TYPE,
  linkPayload,
  plainMessage,
  pointName,
  registerOptions,
  registerPointFormOf,
  registerPointPayload,
  registerPointProblems,
  type PointRecord,
  type RegisterOption,
  type RegisterPointForm,
  type SiatBranchData,
} from './settings';

/** La caja: lista desplegable si se conocen las cajas; si no, su código. */
function RegisterChoice({ registers, value, onChange, emptyLabel, hint }: { registers: readonly RegisterOption[] | null; value: string; onChange: (value: string) => void; emptyLabel: string; hint: string }) {
  if (registers) {
    return <SelectField label="Caja de M-INV" allLabel={emptyLabel} value={value} onChange={(next) => onChange(next)} options={registerOptions(registers)} hint={hint} />;
  }
  return (
    <TextField
      label="Código de la caja"
      value={value}
      onChange={onChange}
      placeholder="CM-CAJA1"
      hint={`${hint} Vacío = ${emptyLabel.toLowerCase()}.`}
      maxLength={40}
      autoCapitalize="characters"
      spellCheck={false}
      optional
    />
  );
}

export interface RegisterPointDialogProps {
  open: boolean;
  /** Sucursales con su código del Padrón. */
  branches: readonly SiatBranchData[];
  registers: readonly RegisterOption[] | null;
  activeBranchId: string | null;
  onClose: () => void;
  onSaved: () => void;
}

export function RegisterPointDialog({ open, branches, registers, activeBranchId, onClose, onSaved }: RegisterPointDialogProps) {
  const formId = useId();
  const register = useRpcCommand('RegisterSiatPointOfSaleCommand', {
    notifyError: false,
    success: (point) => `Punto de venta ${point.code} registrado en ${point.branchCode} · ${point.name}`,
  });
  const [form, setForm] = useState<RegisterPointForm>(() => registerPointFormOf(branches, activeBranchId));
  const [touched, setTouched] = useState(false);
  const [wasOpen, setWasOpen] = useState(false);
  if (open !== wasOpen) {
    setWasOpen(open);
    if (open) {
      setForm(registerPointFormOf(branches, activeBranchId));
      setTouched(false);
    }
  }
  const problems = registerPointProblems(form);

  const set = <K extends keyof RegisterPointForm>(key: K, value: RegisterPointForm[K]) => {
    setForm((current) => ({ ...current, [key]: value }));
    if (register.error) register.reset();
  };

  const close = () => {
    register.reset();
    onClose();
  };

  const submit = async () => {
    setTouched(true);
    if (Object.keys(problems).length > 0) return;
    const outcome = await register.run(registerPointPayload(form));
    if (!outcome.ok) return;
    onSaved();
    close();
  };

  return (
    <Dialog
      open={open}
      onClose={close}
      dismissible={!register.sending}
      size="lg"
      title="Registrar un punto de venta"
      description="El SIN asigna el número del punto. Si la sucursal todavía no tiene su punto 0, se registra también, con su CUIS."
      footer={
        <>
          <Button variant="outline" onClick={close} disabled={register.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<Plus />} loading={register.sending} disabled={branches.length === 0}>
            Registrar en el SIN
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={register.errorText} busy={register.sending}>
        {branches.length === 0 && <Alert tone="warning">Primero asigne el código del Padrón a la sucursal (sección «Sucursales del Padrón»).</Alert>}
        <FormGrid>
          <SelectField
            label="Sucursal"
            allLabel={false}
            placeholder="Elija la sucursal"
            value={form.branchCode}
            onChange={(value) => set('branchCode', value)}
            options={branches.map((branch) => ({ value: branch.branchCode, label: `${branch.branchCode} · ${branch.branchName} (Padrón ${branch.siatCode})` }))}
            error={touched ? problems.branchCode : undefined}
            required
          />
          <TextField label="Nombre del punto" value={form.name} onChange={(value) => set('name', value)} error={touched ? problems.name : undefined} maxLength={100} required />
          <TextField
            label="Descripción"
            className="sm:col-span-2"
            value={form.description}
            onChange={(value) => set('description', value)}
            error={touched ? problems.description : undefined}
            maxLength={200}
            optional
          />
          <RegisterChoice registers={registers} value={form.registerCode} onChange={(value) => set('registerCode', value)} emptyLabel="Sin caja (se vincula después)" hint="La caja que factura con este punto." />
          <NumberField
            label="Tipo de punto de venta"
            value={form.typeCode}
            onChange={(value) => set('typeCode', value)}
            error={touched ? problems.typeCode : undefined}
            hint={`${DEFAULT_POINT_TYPE} = cajeros (el más común).`}
            required
          />
        </FormGrid>
      </Form>
    </Dialog>
  );
}

export interface LinkRegisterDialogProps {
  /** null = cerrado. */
  point: PointRecord | null;
  registers: readonly RegisterOption[] | null;
  onClose: () => void;
  onSaved: () => void;
}

export function LinkRegisterDialog({ point, registers, onClose, onSaved }: LinkRegisterDialogProps) {
  const formId = useId();
  const link = useRpcCommand('LinkPointOfSaleRegisterCommand', { notifyError: false, success: (message) => plainMessage(message) || 'Caja vinculada' });
  const [shown, setShown] = useState<PointRecord | null>(null);
  const [code, setCode] = useState('');
  const [wasOpen, setWasOpen] = useState(false);
  if ((point !== null) !== wasOpen) {
    setWasOpen(point !== null);
    if (point) {
      setShown(point);
      setCode(point.registerCode ?? '');
    }
  }

  const close = () => {
    link.reset();
    onClose();
  };

  const submit = async () => {
    if (!shown) return;
    const outcome = await link.run(linkPayload(shown.id, code));
    if (!outcome.ok) return;
    onSaved();
    close();
  };

  return (
    <Dialog
      open={point !== null}
      onClose={close}
      dismissible={!link.sending}
      title="Vincular una caja"
      description={shown ? `${shown.branchCode} · ${pointName(shown)}` : undefined}
      footer={
        <>
          <Button variant="outline" onClick={close} disabled={link.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<Link2 />} loading={link.sending}>
            Guardar
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={link.errorText} busy={link.sending}>
        <p className="text-sm text-text-muted">La caja elegida factura con este punto de venta. Una caja factura con un solo punto por ambiente: si estaba en otro, se desvincula de él.</p>
        <RegisterChoice
          registers={registers}
          value={code}
          onChange={(value) => {
            setCode(value);
            if (link.error) link.reset();
          }}
          emptyLabel="Sin caja (desvincular)"
          hint="Sin caja, el punto lo usan la oficina y la API."
        />
      </Form>
    </Dialog>
  );
}
