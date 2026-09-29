// Módulo «Configuración» · pestaña «Empresa» (la pestaña «Empresa» de Usuarios y roles del escritorio): los datos de la
// empresa (`GetCompanySettingsQuery`) y los parámetros del semáforo de stock en listas desplegables, que cambia quien
// administra usuarios (`UpdateCompanySettingsCommand`): el margen de alerta y los días sin rotación.

import { Building2, RotateCcw, Save, SlidersHorizontal } from 'lucide-react';
import { useId, useState } from 'react';
import { usePermissions, useRpcCommand, useRpcQuery } from '@/4-presentation/panel/hooks';
import { Button, DetailList, Dialog, ErrorState, Form, LoadingState, Section, SelectField } from '@/4-presentation/panel/kit';
import { formatDate, formatNumber } from '@/4-presentation/panel/lib';
import { companyParametersPayload, daysOptions, marginOptions, marginText, type CompanyData } from './settings';

export function CompanyTab() {
  const { canRun } = usePermissions();
  const canEdit = canRun('UpdateCompanySettingsCommand');
  const company = useRpcQuery('GetCompanySettingsQuery', {});
  const [editing, setEditing] = useState(false);

  if (company.error && !company.data) return <ErrorState error={company.error} operation="GetCompanySettingsQuery" onRetry={company.reload} retrying={company.fetching} />;
  if (!company.data) return <LoadingState label="Cargando los datos de la empresa…" rows={3} />;
  const data = company.data;

  return (
    <div className="space-y-5">
      <Section
        title="Datos de la empresa"
        description="Se definen al instalar M-INV y van en los reportes y documentos."
        actions={
          <Button variant="outline" leftIcon={<RotateCcw />} loading={company.fetching} onClick={company.reload}>
            Actualizar
          </Button>
        }
      >
        <DetailList
          items={[
            { label: 'Razón social', value: data.legalName },
            { label: 'Código de empresa', value: data.code },
            { label: 'NIT', value: data.taxId },
            { label: 'Zona horaria', value: data.timeZoneId },
            { label: 'Fecha mínima para registrar operaciones', value: formatDate(data.minBusinessDate) },
          ]}
        />
      </Section>

      <Section
        title="Semáforo de stock"
        description="Cuándo un producto se marca como bajo o sin rotación en el stock, las alertas, el pedido sugerido y los reportes."
        actions={
          canEdit && (
            <Button leftIcon={<SlidersHorizontal />} onClick={() => setEditing(true)}>
              Cambiar parámetros
            </Button>
          )
        }
      >
        <DetailList
          columns={1}
          items={[
            {
              label: 'Margen de alerta',
              value: `${marginText(data.alertMargin)} sobre el mínimo: un producto pasa a «bajo» cuando su stock queda dentro de ese margen del mínimo.`,
            },
            {
              label: 'Días sin rotación',
              value: `${formatNumber(data.daysWithoutRotation)} días: los productos sin movimiento en ese tiempo aparecen «sin rotación» en alertas y reportes.`,
            },
          ]}
        />
      </Section>

      <ParametersDialog company={editing ? data : null} onClose={() => setEditing(false)} onSaved={company.reload} />
    </div>
  );
}

interface ParametersDialogProps {
  /** null = cerrado. */
  company: CompanyData | null;
  onClose: () => void;
  onSaved: () => void;
}

function ParametersDialog({ company, onClose, onSaved }: ParametersDialogProps) {
  const formId = useId();
  const update = useRpcCommand('UpdateCompanySettingsCommand', {
    notifyError: false,
    success: (_result, payload) => `Parámetros guardados: alerta ${marginText(payload.alertMargin)} · rotación ${formatNumber(payload.daysWithoutRotation)} días`,
  });
  const [shown, setShown] = useState<CompanyData | null>(null);
  const [margin, setMargin] = useState('');
  const [days, setDays] = useState('');
  const [wasOpen, setWasOpen] = useState(false);
  // Cada vez que se abre, empieza con los valores guardados.
  if ((company !== null) !== wasOpen) {
    setWasOpen(company !== null);
    if (company) {
      setShown(company);
      setMargin(String(company.alertMargin));
      setDays(String(company.daysWithoutRotation));
    }
  }

  const close = () => {
    update.reset();
    onClose();
  };

  const submit = async () => {
    if (!margin || !days) return;
    const outcome = await update.run(companyParametersPayload(margin, days));
    if (!outcome.ok) return;
    onSaved();
    close();
  };

  return (
    <Dialog
      open={company !== null}
      onClose={close}
      dismissible={!update.sending}
      title="Parámetros del semáforo"
      description={shown?.legalName}
      footer={
        <>
          <Button variant="outline" onClick={close} disabled={update.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<Save />} loading={update.sending}>
            Guardar parámetros
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={update.errorText} busy={update.sending}>
        <SelectField
          label="Margen de alerta"
          allLabel={false}
          value={margin}
          onChange={(value) => setMargin(value)}
          options={marginOptions(shown?.alertMargin ?? null)}
          hint="Un producto pasa a «bajo» cuando su stock queda a este porcentaje del mínimo (semáforo y pedido sugerido)."
          required
        />
        <SelectField
          label="Días sin rotación"
          allLabel={false}
          value={days}
          onChange={(value) => setDays(value)}
          options={daysOptions(shown?.daysWithoutRotation ?? null)}
          hint="Los productos sin movimiento durante estos días aparecen como «sin rotación» en alertas y reportes."
          required
        />
        <p className="flex items-start gap-2 text-sm text-text-muted">
          <Building2 aria-hidden="true" className="mt-0.5 size-4 shrink-0" />
          El semáforo y el pedido sugerido se recalculan con los parámetros nuevos para todas las sucursales.
        </p>
      </Form>
    </Dialog>
  );
}
