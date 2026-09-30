// Módulo «Catálogo» · crear o modificar una especificación de una categoría (SpecsAdminDialog del escritorio) con
// `SaveSpecDefinitionCommand`: nombre, código (se sugiere del nombre), tipo (texto, número con unidad u opción), varios
// valores, opciones (una por renglón), filtrable, obligatoria, clave del armador de PC y orden. El tipo, el multivalor, la
// categoría y el código no cambian después de crearla; una opción que usa algún producto no se puede quitar (lo decide el
// servidor y el error se muestra aquí).

import { FilePlus2, Save } from 'lucide-react';
import { useId, useState } from 'react';
import { useRpcCommand } from '@/4-presentation/panel/hooks';
import { Alert, Button, Checkbox, Dialog, Form, FormGrid, NumberField, RadioGroup, SelectField, TextArea, TextField, useNotify } from '@/4-presentation/panel/kit';
import type { Option, SpecDefinitionRecord } from './catalog';
import { SPEC_LIMITS, SPEC_TYPE_OPTIONS, specDraftOf, specProblems, suggestSpecCode, toSaveSpec, type SpecDataTypeValue, type SpecDraft } from './specs';

export interface SpecTarget {
  /** null = especificación nueva. */
  definition: SpecDefinitionRecord | null;
  /** Categoría sugerida para una nueva. */
  categoryCode: string;
  /** Orden sugerido para una nueva. */
  sortOrder: number;
}

export interface SpecDialogProps {
  target: SpecTarget | null;
  categories: readonly Option[];
  /** Claves del armador que se pueden elegir. */
  compatibilityKeys: readonly Option[];
  onClose: () => void;
  onSaved: (code: string, categoryCode: string) => void;
}

export function SpecDialog({ target, categories, compatibilityKeys, onClose, onSaved }: SpecDialogProps) {
  const formId = useId();
  const notify = useNotify();
  const [initial] = useState(target);
  const definition = initial?.definition ?? null;
  const isNew = definition === null;
  const [draft, setDraft] = useState<SpecDraft>(() => specDraftOf(definition, initial?.categoryCode ?? '', initial?.sortOrder ?? 10));
  const [touched, setTouched] = useState(false);
  const save = useRpcCommand('SaveSpecDefinitionCommand', { notifyError: false });
  const problems = specProblems(draft, isNew);
  const shown = (field: keyof typeof problems) => (touched ? problems[field] : undefined);

  const change = (next: Partial<SpecDraft>) => {
    setDraft((current) => ({ ...current, ...next }));
    save.reset();
  };

  const submit = async () => {
    setTouched(true);
    if (Object.keys(problems).length > 0) return;
    const payload = toSaveSpec(draft);
    const outcome = await save.run(payload);
    if (!outcome.ok) return;
    const categoryName = categories.find((category) => category.value === payload.categoryCode)?.label ?? payload.categoryCode;
    notify.success(isNew ? 'Especificación creada' : 'Especificación guardada', `${payload.name} (${outcome.result}) · ${categoryName}`);
    onSaved(outcome.result, payload.categoryCode);
    onClose();
  };

  return (
    <Dialog
      open={target !== null}
      onClose={onClose}
      dismissible={!save.sending}
      size="lg"
      title={isNew ? 'Nueva especificación' : `Modificar «${definition.name}»`}
      description={
        isNew
          ? 'Cada categoría define sus especificaciones y sus subcategorías las heredan. Las filtrables alimentan los filtros del catálogo y de la caja; las que tienen clave del armador las usa el armador de PC.'
          : `De ${definition.categoryName}: el cambio vale para esa categoría y todas sus subcategorías.`
      }
      footer={
        <>
          <Button variant="outline" onClick={onClose} disabled={save.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={isNew ? <FilePlus2 /> : <Save />} loading={save.sending}>
            {isNew ? 'Crear especificación' : 'Guardar cambios'}
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={save.errorText} busy={save.sending}>
        <FormGrid>
          <SelectField
            label="Categoría"
            allLabel={false}
            placeholder="Elija una categoría"
            value={draft.categoryCode}
            onChange={(categoryCode) => change({ categoryCode })}
            options={categories}
            error={shown('categoryCode')}
            required
            disabled={!isNew}
            hint={isNew ? 'Sus subcategorías la heredan.' : 'No cambia después de crearla.'}
          />
          <TextField
            label="Nombre"
            value={draft.name}
            onChange={(name) => change(draft.codeTouched || !isNew ? { name } : { name, code: suggestSpecCode(name) })}
            error={shown('name')}
            required
            maxLength={SPEC_LIMITS.name}
            placeholder="Ej.: Socket"
            data-autofocus
          />
          <TextField
            label="Código"
            value={draft.code}
            onChange={(code) => change({ code: code.toLowerCase(), codeTouched: true })}
            error={shown('code')}
            required={isNew}
            disabled={!isNew}
            maxLength={SPEC_LIMITS.code}
            autoComplete="off"
            spellCheck={false}
            hint={isNew ? 'Se sugiere del nombre. No se repite en la categoría, sus madres ni sus subcategorías.' : 'No cambia después de crearla.'}
          />
          <TextField
            label="Unidad"
            value={draft.unit}
            onChange={(unit) => change({ unit })}
            error={shown('unit')}
            optional
            maxLength={SPEC_LIMITS.unit}
            placeholder="GB, W, mm, Hz"
            hint="Para las de tipo número."
          />
        </FormGrid>
        <RadioGroup<SpecDataTypeValue>
          label="Tipo"
          value={draft.dataType}
          onChange={(dataType) => change({ dataType })}
          options={SPEC_TYPE_OPTIONS}
          orientation="horizontal"
          disabled={!isNew}
          hint={isNew ? undefined : 'El tipo no cambia después de crearla.'}
        />
        <Checkbox
          label="Admite varios valores"
          description={draft.dataType === 'Option' ? 'Se eligen varias opciones (por ejemplo, los sockets de un disipador).' : 'Se escriben separados por comas.'}
          checked={draft.isMultiValued}
          onChange={(isMultiValued) => change({ isMultiValued })}
          disabled={!isNew}
        />
        {draft.dataType === 'Option' && (
          <TextArea
            label="Opciones"
            value={draft.options}
            onChange={(options) => change({ options })}
            error={shown('options')}
            required
            rows={5}
            hint="Una por renglón, en el orden en que se muestran. Una opción que ya usa un producto no se puede quitar."
          />
        )}
        <FormGrid>
          <Checkbox label="Filtra el catálogo" description="Aparece como filtro en el catálogo y en la caja." checked={draft.isFilterable} onChange={(isFilterable) => change({ isFilterable })} />
          <Checkbox label="Obligatoria" description="Todo producto de la categoría debe tenerla en su ficha." checked={draft.isRequired} onChange={(isRequired) => change({ isRequired })} />
          <SelectField
            label="Clave del armador de PC"
            allLabel="No participa en el armador"
            value={draft.compatibilityKey}
            onChange={(compatibilityKey) => change({ compatibilityKey })}
            options={compatibilityKeys}
            hint="La compatibilidad la decide el servidor con estas claves."
          />
          <NumberField label="Orden" value={draft.sortOrder} onChange={(sortOrder) => change({ sortOrder })} error={shown('sortOrder')} required hint="10, 20, 30… (de menor a mayor en la ficha)." />
        </FormGrid>
        {!isNew && definition.isInherited && (
          <Alert tone="info">Se hereda en la categoría que está viendo: se modifica en {definition.categoryName} y el cambio llega a todas sus subcategorías.</Alert>
        )}
      </Form>
    </Dialog>
  );
}
