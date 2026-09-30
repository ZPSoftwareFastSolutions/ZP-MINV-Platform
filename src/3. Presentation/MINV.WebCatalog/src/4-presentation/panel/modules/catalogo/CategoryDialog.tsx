// Módulo «Catálogo» · nueva categoría, subcategoría (dentro de otra: hereda sus especificaciones) o cambio de nombre, con
// `SaveCategoryCommand`. El código se sugiere solo a partir del nombre («Periféricos» → PER), como el escritorio, y se
// puede cambiar antes de crearla; una categoría existente no cambia de código ni de madre.

import { FolderPlus, Save } from 'lucide-react';
import { useId, useState } from 'react';
import { useRpcCommand } from '@/4-presentation/panel/hooks';
import { Button, Dialog, Form, SelectField, TextField, useNotify } from '@/4-presentation/panel/kit';
import type { Option } from './catalog';
import { CATEGORY_LIMITS, categoryDraftOf, categoryProblems, toSaveCategory, withCategoryName, type CategoryDraft, type CategoryMode } from './categories';

export interface CategoryTarget {
  mode: CategoryMode;
  /** La categoría que se renombra o la madre de la subcategoría. */
  category: { code: string; name: string } | null;
}

export interface CategoryDialogProps {
  /** null = cerrado. */
  target: CategoryTarget | null;
  /** Todas las categorías (para la madre y para no repetir el código). */
  categories: readonly Option[];
  onClose: () => void;
  onSaved: (code: string) => void;
}

export function CategoryDialog({ target, categories, onClose, onSaved }: CategoryDialogProps) {
  const formId = useId();
  const notify = useNotify();
  const [initial] = useState(target);
  const mode = initial?.mode ?? 'nueva';
  const [draft, setDraft] = useState<CategoryDraft>(() => categoryDraftOf(mode, initial?.category ?? null));
  const [touched, setTouched] = useState(false);
  const save = useRpcCommand('SaveCategoryCommand', { notifyError: false });
  const existing = categories.map((category) => category.value);
  const problems = categoryProblems(draft, mode, existing);

  const submit = async () => {
    setTouched(true);
    if (Object.keys(problems).length > 0) return;
    const outcome = await save.run(toSaveCategory(draft, mode));
    if (!outcome.ok) return;
    const name = draft.name.trim();
    notify.success(mode === 'renombrar' ? 'Categoría actualizada' : mode === 'subcategoria' ? 'Subcategoría creada' : 'Categoría creada', `${name} (${outcome.result})`);
    onSaved(outcome.result);
    onClose();
  };

  const parentName = categories.find((category) => category.value === draft.parentCode)?.label;
  const title = mode === 'renombrar' ? `Cambiar el nombre de «${initial?.category?.name ?? ''}»` : mode === 'subcategoria' ? `Nueva subcategoría de ${initial?.category?.name ?? ''}` : 'Nueva categoría';

  return (
    <Dialog
      open={target !== null}
      onClose={onClose}
      dismissible={!save.sending}
      title={title}
      description={
        mode === 'renombrar'
          ? `Código ${draft.code}: no cambia. Los productos siguen en la categoría.`
          : 'Queda disponible para todos los productos. Una subcategoría hereda las especificaciones de su categoría madre.'
      }
      footer={
        <>
          <Button variant="outline" onClick={onClose} disabled={save.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={mode === 'renombrar' ? <Save /> : <FolderPlus />} loading={save.sending}>
            {mode === 'renombrar' ? 'Guardar nombre' : 'Crear categoría'}
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={save.errorText} busy={save.sending}>
        <TextField
          label="Nombre"
          value={draft.name}
          onChange={(name) => {
            setDraft((current) => (mode === 'renombrar' ? { ...current, name } : withCategoryName(current, name, existing)));
            save.reset();
          }}
          error={touched ? problems.name : undefined}
          required
          maxLength={CATEGORY_LIMITS.name}
          placeholder={mode === 'subcategoria' ? 'Ej.: Tarjetas de video NVIDIA' : 'Ej.: Periféricos'}
          data-autofocus
        />
        {mode !== 'renombrar' && (
          <>
            <TextField
              label="Código"
              value={draft.code}
              onChange={(code) => {
                setDraft((current) => ({ ...current, code: code.toUpperCase(), codeTouched: true }));
                save.reset();
              }}
              error={touched ? problems.code : undefined}
              required
              maxLength={CATEGORY_LIMITS.code}
              autoComplete="off"
              spellCheck={false}
              hint="Se sugiere solo a partir del nombre. Letras, números y guiones; no cambia después de crearla."
            />
            <SelectField
              label="Categoría madre"
              allLabel="Ninguna (categoría principal)"
              value={draft.parentCode}
              onChange={(parentCode) => setDraft((current) => ({ ...current, parentCode }))}
              options={categories}
              disabled={mode === 'subcategoria'}
              hint={parentName ? `Hereda las especificaciones de ${parentName}.` : 'Sin madre: es una categoría principal.'}
            />
          </>
        )}
      </Form>
    </Dialog>
  );
}
