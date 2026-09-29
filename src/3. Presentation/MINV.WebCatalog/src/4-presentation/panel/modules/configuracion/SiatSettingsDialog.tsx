// Módulo «Configuración» · datos del Padrón y del sistema autorizado (`SaveSiatSettingsCommand`), como el escritorio: NIT,
// razón social, código de sistema, ambiente (pruebas o producción), leyendas y si la facturación está activa. Pasar a
// PRODUCCIÓN pide una confirmación aparte (desde ahí cada factura tiene valor legal). Si se activa sin lo que exige el
// servidor (token del ambiente y casa matriz), se avisa antes; el servidor decide igual y su mensaje se ve aquí.

import { Save } from 'lucide-react';
import { useId, useState } from 'react';
import { useRpcCommand } from '@/4-presentation/panel/hooks';
import { Alert, Button, ConfirmDialog, Dialog, Form, FormGrid, RadioGroup, Switch, TextArea, TextField } from '@/4-presentation/panel/kit';
import {
  PRODUCTION,
  TESTING,
  goesToProduction,
  plainMessage,
  readinessChecks,
  siatSettingsFormOf,
  siatSettingsPayload,
  siatSettingsProblems,
  type SiatSettingsData,
  type SiatSettingsField,
  type SiatSettingsForm,
} from './settings';

export interface SiatSettingsDialogProps {
  open: boolean;
  view: SiatSettingsData;
  /** Nombre de la empresa (razón social propuesta si todavía no hay una). */
  companyName: string;
  onClose: () => void;
  onSaved: () => void;
}

const ENVIRONMENT_OPTIONS = [
  { value: String(TESTING), label: 'Pruebas y piloto (2)', description: 'Sin valor legal: las facturas se imprimen con «SIN VALOR LEGAL».' },
  { value: String(PRODUCTION), label: 'Producción (1)', description: 'Cada factura vale ante el SIN. Solo después de aprobar el piloto y la inspección.' },
];

export function SiatSettingsDialog({ open, view, companyName, onClose, onSaved }: SiatSettingsDialogProps) {
  const formId = useId();
  const save = useRpcCommand('SaveSiatSettingsCommand', { notifyError: false, success: (message) => plainMessage(message) || 'Facturación guardada' });
  const [form, setForm] = useState<SiatSettingsForm>(() => siatSettingsFormOf(view, companyName));
  const [touched, setTouched] = useState(false);
  const [confirming, setConfirming] = useState(false);
  const [wasOpen, setWasOpen] = useState(false);
  if (open !== wasOpen) {
    setWasOpen(open);
    if (open) {
      setForm(siatSettingsFormOf(view, companyName));
      setTouched(false);
      setConfirming(false);
    }
  }
  const problems = siatSettingsProblems(form);
  const show = (field: SiatSettingsField) => (touched ? problems[field] : undefined);
  const missing = form.enabled ? readinessChecks(view, Number(form.environment)).filter((check) => !check.ok && check.key !== 'padron') : [];

  const set = <K extends keyof SiatSettingsForm>(key: K, value: SiatSettingsForm[K]) => {
    setForm((current) => ({ ...current, [key]: value }));
    if (save.error) save.reset();
  };

  const close = () => {
    save.reset();
    setConfirming(false);
    onClose();
  };

  const send = async () => {
    const outcome = await save.run(siatSettingsPayload(form));
    if (outcome.ok) {
      onSaved();
      close();
    }
    return outcome;
  };

  const submit = async () => {
    setTouched(true);
    if (Object.keys(problems).length > 0) return;
    if (goesToProduction(view, form)) {
      setConfirming(true);
      return;
    }
    await send();
  };

  return (
    <>
      <Dialog
        open={open}
        onClose={close}
        dismissible={!save.sending}
        size="lg"
        title="Datos del Padrón y del sistema"
        description="Como figuran en el Padrón Nacional de Contribuyentes y en la autorización del sistema de facturación."
        footer={
          <>
            <Button variant="outline" onClick={close} disabled={save.sending}>
              Cancelar
            </Button>
            <Button type="submit" form={formId} leftIcon={<Save />} loading={save.sending && !confirming}>
              Guardar los datos
            </Button>
          </>
        }
      >
        <Form id={formId} onSubmit={submit} error={confirming ? undefined : save.errorText} busy={save.sending}>
          <FormGrid>
            <TextField
              label="NIT de la empresa"
              inputMode="numeric"
              value={form.nit}
              onChange={(value) => set('nit', value)}
              error={show('nit')}
              maxLength={13}
              autoComplete="off"
              required
              data-autofocus
            />
            <TextField label="Código de sistema" value={form.systemCode} onChange={(value) => set('systemCode', value)} error={show('systemCode')} hint="Lo asigna el SIN al autorizar M-INV." maxLength={50} inputClassName="font-mono" required />
            <TextField
              label="Razón social"
              className="sm:col-span-2"
              value={form.businessName}
              onChange={(value) => set('businessName', value)}
              error={show('businessName')}
              hint="Tal como figura en el Padrón."
              maxLength={200}
              required
            />
          </FormGrid>
          <RadioGroup label="Ambiente" value={form.environment} onChange={(value) => set('environment', value)} options={ENVIRONMENT_OPTIONS} required />
          <TextArea label="Leyenda en línea" value={form.onlineLegend} onChange={(value) => set('onlineLegend', value)} error={show('onlineLegend')} hint="Tercera leyenda de la factura. Vacía = la oficial." maxLength={250} rows={3} />
          <TextArea label="Leyenda fuera de línea" value={form.offlineLegend} onChange={(value) => set('offlineLegend', value)} error={show('offlineLegend')} hint="Vacía = la oficial." maxLength={250} rows={3} />
          <Switch
            label="Facturación activa: las ventas emiten factura del SIN"
            description="Desactivada, las ventas salen sin documento fiscal."
            checked={form.enabled}
            onChange={(value) => set('enabled', value)}
          />
          {missing.length > 0 && (
            <Alert tone="warning" title="Para activarla todavía falta">
              <ul className="list-disc pl-5">
                {missing.map((check) => (
                  <li key={check.key}>{check.label}</li>
                ))}
              </ul>
            </Alert>
          )}
        </Form>
      </Dialog>

      <ConfirmDialog
        open={confirming}
        onClose={() => setConfirming(false)}
        tone="danger"
        title="¿Pasar a PRODUCCIÓN?"
        message="En producción cada factura tiene VALOR LEGAL ante el SIN. Úselo solo después de aprobar las pruebas del piloto y la inspección del SIN, con el token y las direcciones de producción."
        confirmLabel="Usar producción"
        onConfirm={send}
        error={save.errorText}
      />
    </>
  );
}
