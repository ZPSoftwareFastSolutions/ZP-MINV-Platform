// Módulo «Catálogo» · pestaña «Ficha técnica» del formulario del producto (TechSheetEditor del escritorio): control por
// serie o IMEI, meses de garantía y cada especificación de la categoría (propias y heredadas) con el control de su tipo:
// número con su unidad, texto, una opción (lista desplegable) o varias (casillas). Las de varios valores de texto o
// número se escriben separadas por comas. Sin `catalog.specs.manage` la ficha se ve pero no se edita.

import type { RpcQuery } from '@/4-presentation/panel/hooks';
import { Alert, Checkbox, DetailList, ErrorState, FieldGroup, LoadingState, NumberField, RadioGroup, SelectField, Switch, TextField } from '@/4-presentation/panel/kit';
import { permissionName } from '@/4-presentation/app/contract';
import { warrantyText, type SpecDefinitionRecord } from './catalog';
import { SERIAL_KIND_OPTIONS, compatibilityLabel, controlOf, serialHelp, specTypeText, type ProductTechRecord, type SpecValueDraft, type TechDraft, type TechProblems } from './specs';

export interface TechSheetFieldsProps {
  categoryName: string | null;
  /** Especificaciones de la categoría elegida (null si todavía no hay categoría). */
  definitions: RpcQuery<SpecDefinitionRecord[]> | null;
  draft: TechDraft | null;
  /** La ficha guardada (para el texto del control por serie). */
  saved: ProductTechRecord | undefined;
  onChange: (next: TechDraft) => void;
  /** Problemas a la vista (después de intentar guardar). */
  problems: TechProblems | null;
  canEdit: boolean;
}

function helpOf(definition: SpecDefinitionRecord): string {
  return [
    specTypeText(definition.dataType, definition.isMultiValued),
    definition.isInherited ? `de ${definition.categoryName}` : null,
    definition.compatibilityKey ? `la usa el armador de PC (${compatibilityLabel(definition.compatibilityKey).toLowerCase()})` : null,
    definition.isFilterable ? 'filtra el catálogo' : null,
    definition.isMultiValued && definition.dataType !== 'Option' ? 'valores separados por comas' : null,
  ]
    .filter(Boolean)
    .join(' · ');
}

export function TechSheetFields({ categoryName, definitions, draft, saved, onChange, problems, canEdit }: TechSheetFieldsProps) {
  if (!definitions) return <p className="text-sm text-text-muted">Elija primero la categoría del producto (pestaña «Datos»): la ficha técnica depende de ella.</p>;
  if (definitions.error) return <ErrorState error={definitions.error} operation="GetSpecDefinitionsQuery" onRetry={definitions.reload} retrying={definitions.fetching} />;
  if (!definitions.data || !draft) return <LoadingState label="Leyendo la ficha técnica…" rows={3} />;
  const list = definitions.data;

  if (!canEdit) {
    return (
      <div className="space-y-4">
        <Alert tone="info">Para cambiar la ficha técnica hace falta el permiso «{permissionName('catalog.specs.manage')}».</Alert>
        <DetailList
          items={[
            { label: 'Control por unidad', value: draft.trackSerials ? (draft.serialKind === 'Imei' ? 'IMEI' : 'Número de serie') : 'Sin serie (por cantidad)' },
            { label: 'Garantía', value: warrantyText(draft.warrantyMonths ?? 0) },
            ...(saved?.specs ?? []).map((spec) => ({ label: spec.name, value: spec.display || spec.values.join(', ') })),
          ]}
        />
      </div>
    );
  }

  const setValue = (code: string, value: SpecValueDraft) => onChange({ ...draft, values: { ...draft.values, [code]: value } });
  const entryOf = (code: string): SpecValueDraft => draft.values[code] ?? { text: '', picked: [] };

  return (
    <div className="space-y-5" data-testid="ficha-formulario">
      <FieldGroup label="Control por unidad y garantía" bodyClassName="space-y-3">
        <Switch
          label="Lleva serie o IMEI (una por unidad)"
          description={serialHelp(draft, saved)}
          checked={draft.trackSerials}
          onChange={(trackSerials) => onChange({ ...draft, trackSerials })}
        />
        {draft.trackSerials && (
          <RadioGroup label="Tipo de identificador" value={draft.serialKind} onChange={(serialKind) => onChange({ ...draft, serialKind })} options={SERIAL_KIND_OPTIONS} />
        )}
        <NumberField
          label="Meses de garantía"
          value={draft.warrantyMonths}
          onChange={(warrantyMonths) => onChange({ ...draft, warrantyMonths })}
          unit="meses"
          error={problems?.warranty}
          hint={draft.warrantyMonths && draft.warrantyMonths > 0 ? `${warrantyText(draft.warrantyMonths)}: se imprime «Garantía hasta…» en el ticket y la factura (fecha de la venta + meses).` : 'De 0 a 120. Con 0, el producto no tiene garantía.'}
          required
        />
      </FieldGroup>

      <div className="space-y-3">
        <h3 className="text-sm font-semibold text-text">Especificaciones{categoryName ? ` de ${categoryName}` : ''}</h3>
        {list.length === 0 ? (
          <p className="text-sm text-text-muted">
            La categoría {categoryName ?? ''} todavía no tiene especificaciones: se definen en la pestaña «Especificaciones» del catálogo.
          </p>
        ) : (
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            {list.map((definition) => {
              const entry = entryOf(definition.code);
              const error = problems?.specs[definition.code];
              const control = controlOf(definition);
              if (control === 'option-many') {
                return (
                  <FieldGroup key={definition.code} label={definition.name} required={definition.isRequired} hint={helpOf(definition)} error={error} className="sm:col-span-2" bodyClassName="grid grid-cols-1 gap-1 sm:grid-cols-2">
                    {definition.options.map((option) => (
                      <Checkbox
                        key={option}
                        label={option}
                        checked={entry.picked.some((value) => value.toLowerCase() === option.toLowerCase())}
                        onChange={(checked) =>
                          setValue(definition.code, {
                            ...entry,
                            picked: checked ? [...entry.picked, option] : entry.picked.filter((value) => value.toLowerCase() !== option.toLowerCase()),
                          })
                        }
                      />
                    ))}
                  </FieldGroup>
                );
              }
              if (control === 'option-one') {
                const current = definition.options.find((option) => entry.picked.some((value) => value.toLowerCase() === option.toLowerCase())) ?? '';
                return (
                  <SelectField
                    key={definition.code}
                    label={definition.name}
                    required={definition.isRequired}
                    hint={helpOf(definition)}
                    error={error}
                    allLabel="(sin valor)"
                    value={current}
                    onChange={(value) => setValue(definition.code, { ...entry, picked: value ? [value] : [] })}
                    options={definition.options.map((option) => ({ value: option, label: option }))}
                  />
                );
              }
              return (
                <TextField
                  key={definition.code}
                  label={definition.unit ? `${definition.name} (${definition.unit})` : definition.name}
                  required={definition.isRequired}
                  hint={helpOf(definition)}
                  error={error}
                  value={entry.text}
                  onChange={(text) => setValue(definition.code, { ...entry, text })}
                  inputMode={control === 'number' && !definition.isMultiValued ? 'decimal' : undefined}
                  placeholder={definition.isMultiValued ? 'Valores separados por comas' : control === 'number' ? '0' : undefined}
                  maxLength={200}
                />
              );
            })}
          </div>
        )}
      </div>
    </div>
  );
}

