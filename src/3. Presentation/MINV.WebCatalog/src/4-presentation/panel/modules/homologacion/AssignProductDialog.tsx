// Módulo «Homologación» · asignar a un producto su actividad económica y su código de producto del SIN
// (`SaveProductHomologationCommand`, permiso «configurar la facturación»), como `AssignProductDialog` del escritorio: se
// elige la actividad, se busca en el catálogo del SIN (`SearchSiatProductsQuery`, empieza con la primera palabra del
// nombre) y se marca el producto del SIN. El error del servidor se muestra dentro del diálogo.
// La pantalla lo monta de nuevo en cada apertura (le cambia la `key`).

import { Save } from 'lucide-react';
import { useId, useState } from 'react';
import { useRpcCommand, useRpcQuery } from '@/4-presentation/panel/hooks';
import { Alert, Button, Dialog, ErrorState, Form, LoadingState, RadioGroup, SearchField, SelectField, useNotify } from '@/4-presentation/panel/kit';
import { activityOptions, defaultActivity, firstWord, plainMessage, sentenceCase, sinText, type ActivityData, type ProductRowData } from './homologation';

export interface AssignProductDialogProps {
  /** null = cerrado. */
  target: ProductRowData | null;
  activities: readonly ActivityData[];
  onClose: () => void;
  onDone: () => void;
}

/** Cuántos productos del SIN se muestran por búsqueda. */
const MAX_RESULTS = 100;

export function AssignProductDialog({ target, activities, onClose, onDone }: AssignProductDialogProps) {
  const formId = useId();
  const notify = useNotify();
  const [shown] = useState(target);
  const current = target ?? shown;
  const open = target !== null;
  const [activity, setActivity] = useState(() => defaultActivity(activities, current?.activityCode));
  const [text, setText] = useState(() => firstWord(current?.name ?? ''));
  const [picked, setPicked] = useState<string | null>(null);
  const [touched, setTouched] = useState(false);
  const results = useRpcQuery('SearchSiatProductsQuery', { activityCode: activity || null, text: text || null, max: MAX_RESULTS }, { enabled: open });
  const save = useRpcCommand('SaveProductHomologationCommand', { notifyError: false });

  if (!current) return null;
  const found = results.data ?? [];
  // Lo elegido; si no se eligió nada, el código actual del producto (si aparece) o el único resultado.
  const chosen = picked ?? (found.some((item) => item.productCode === current.sinProductCode) ? String(current.sinProductCode) : found.length === 1 ? String(found[0].productCode) : null);
  const selected = found.find((item) => String(item.productCode) === chosen) ?? null;
  const noActivities = activities.filter((item) => item.isCurrent).length === 0;

  const submit = async () => {
    setTouched(true);
    if (!selected) return;
    const outcome = await save.run({ items: [{ sku: current.sku, activityCode: selected.activityCode, sinProductCode: selected.productCode }] });
    if (!outcome.ok) return;
    notify.success('Producto homologado', `${current.sku} · ${current.name} → ${sinText(selected.productCode, selected.description)}. ${plainMessage(outcome.result)}`.trim());
    onDone();
    onClose();
  };

  let list;
  if (results.error) list = <ErrorState error={results.error} operation="SearchSiatProductsQuery" onRetry={results.reload} retrying={results.fetching} />;
  else if (!results.data) list = <LoadingState label="Buscando en el catálogo del SIN…" rows={2} />;
  else if (found.length === 0) list = <p className="text-sm text-text-muted">Ningún producto del SIN coincide: pruebe con otra palabra o con otra actividad.</p>;
  else
    list = (
      <div className="max-h-[40vh] overflow-y-auto rounded-xl border border-border px-3" data-testid="productos-sin">
        <RadioGroup
          label={`Productos del SIN (${found.length}${found.length === MAX_RESULTS ? ' o más' : ''})`}
          hideLabel
          value={chosen}
          onChange={(value) => {
            setPicked(value);
            if (save.error) save.reset();
          }}
          options={found.map((item) => ({
            value: String(item.productCode),
            label: `${item.productCode} · ${sentenceCase(item.description)}`,
            description: `Actividad ${item.activityCode}${item.isCurrent ? '' : ' · ya no vigente'}`,
          }))}
          error={touched && !selected ? 'Elija el producto del SIN.' : undefined}
        />
      </div>
    );

  return (
    <Dialog
      open={open}
      onClose={onClose}
      dismissible={!save.sending}
      size="lg"
      title={`Homologar ${current.sku}`}
      description={`${current.name} · ${current.category} · hoy: ${sinText(current.sinProductCode, current.sinProductDescription).toLocaleLowerCase('es')}`}
      footer={
        <>
          <Button variant="outline" onClick={onClose} disabled={save.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<Save />} loading={save.sending} disabled={noActivities}>
            Guardar homologación
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={save.errorText} busy={save.sending}>
        {noActivities && (
          <Alert tone="warning" title="La empresa todavía no tiene actividades económicas sincronizadas">
            Sincronice los catálogos del SIN (Administración › Configuración › Facturación).
          </Alert>
        )}
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <SelectField
            label="Actividad económica"
            allLabel="Todas las actividades"
            value={activity}
            onChange={(value) => {
              setActivity(value);
              setPicked(null);
            }}
            options={activityOptions(activities)}
          />
          <SearchField
            label="Buscar en el catálogo del SIN"
            placeholder="Palabra del producto o código"
            value={text}
            onChange={(value) => {
              setText(value);
              setPicked(null);
            }}
          />
        </div>
        {list}
        {selected && (
          <p className="text-sm" data-testid="eleccion-sin">
            Se guardará: <span className="font-semibold">{sinText(selected.productCode, selected.description)}</span> (actividad {selected.activityCode}).
          </p>
        )}
      </Form>
    </Dialog>
  );
}
