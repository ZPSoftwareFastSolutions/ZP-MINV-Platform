// Módulo «Homologación» · las sugerencias de homologación para aceptar en lote (`SaveProductHomologationCommand`), como
// `SuggestionsDialog` del escritorio: el servidor propuso un código del SIN para cada producto pendiente
// (`SuggestProductHomologationQuery`) buscando por palabras del nombre y la categoría; aquí se revisa cada una (todas
// marcadas al abrir) y se guardan las marcadas con un clic.
// La pantalla lo monta de nuevo en cada apertura (le cambia la `key`).

import { CheckCheck, Sparkles, X } from 'lucide-react';
import { useId, useState } from 'react';
import { useRpcCommand } from '@/4-presentation/panel/hooks';
import { Button, Checkbox, Dialog, Form, useNotify } from '@/4-presentation/panel/kit';
import { plainMessage, type SuggestionItem } from './homologation';

export interface SuggestionsDialogProps {
  /** null = cerrado. */
  target: { activity: string; items: SuggestionItem[] } | null;
  onClose: () => void;
  onDone: () => void;
}

export function SuggestionsDialog({ target, onClose, onDone }: SuggestionsDialogProps) {
  const formId = useId();
  const notify = useNotify();
  const [shown] = useState(target);
  const current = target ?? shown;
  const [items, setItems] = useState<SuggestionItem[]>(() => current?.items ?? []);
  const save = useRpcCommand('SaveProductHomologationCommand', { notifyError: false });

  if (!current) return null;
  const accepted = items.filter((item) => item.accepted);
  const toggleAll = (value: boolean) => setItems((list) => list.map((item) => ({ ...item, accepted: value })));

  const submit = async () => {
    if (accepted.length === 0) return;
    const outcome = await save.run({ items: accepted.map((item) => item.input) });
    if (!outcome.ok) return;
    notify.success('Homologación guardada', plainMessage(outcome.result) || `${accepted.length} productos homologados.`);
    onDone();
    onClose();
  };

  return (
    <Dialog
      open={target !== null}
      onClose={onClose}
      dismissible={!save.sending}
      size="xl"
      title={`Sugerencias de homologación · actividad ${current.activity}`}
      description={`${items.length} propuestas por palabras del nombre y la categoría. Revise cada una: el código del SIN define cómo se informa el producto.`}
      footer={
        <>
          <Button variant="outline" onClick={onClose} disabled={save.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<Sparkles />} loading={save.sending} disabled={accepted.length === 0}>
            Aceptar las marcadas ({accepted.length})
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} error={save.errorText} busy={save.sending}>
        <div className="flex flex-wrap gap-2">
          <Button variant="subtle" leftIcon={<CheckCheck />} onClick={() => toggleAll(true)}>
            Marcar todas
          </Button>
          <Button variant="subtle" leftIcon={<X />} onClick={() => toggleAll(false)}>
            Desmarcar todas
          </Button>
        </div>
        <ul className="divide-y divide-border rounded-xl border border-border px-3" aria-label="Sugerencias" data-testid="sugerencias">
          {items.map((item, index) => (
            <li key={item.input.sku}>
              <Checkbox
                label={item.productText}
                description={`→ ${item.sinText}`}
                checked={item.accepted}
                onChange={(checked) => setItems((list) => list.map((other, position) => (position === index ? { ...other, accepted: checked } : other)))}
              />
            </li>
          ))}
        </ul>
      </Form>
    </Dialog>
  );
}
