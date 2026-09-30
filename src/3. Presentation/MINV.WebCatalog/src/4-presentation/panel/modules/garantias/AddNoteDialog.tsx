// Módulo «Garantías» · «Agregar nota» a un caso abierto (`AddWarrantyClaimNoteCommand`, permiso `service.rma.open`): la
// nota del técnico o de la atención queda en la bitácora del caso con su fecha y usuario. Las notas frecuentes se
// escriben con un clic (se pueden cambiar antes de guardar).
//
// Se monta de nuevo en cada apertura (la pantalla le cambia la `key`): el formulario empieza vacío.

import { NotebookPen } from 'lucide-react';
import { useId, useState } from 'react';
import { useRpcCommand } from '@/4-presentation/panel/hooks';
import { Button, Dialog, Form, TextArea } from '@/4-presentation/panel/kit';
import { NOTE_SUGGESTIONS, TEXT_MAX, noteProblem, type ClaimRecord } from './claims';
import { ServerError } from './ServerError';

export interface AddNoteDialogProps {
  /** El caso (null = cerrado). */
  target: ClaimRecord | null;
  onClose: () => void;
  /** Después de guardar (la pantalla recarga el detalle). */
  onAdded: (row: ClaimRecord) => void;
}

export function AddNoteDialog({ target, onClose, onAdded }: AddNoteDialogProps) {
  const formId = useId();
  const [shown] = useState(target);
  const [note, setNote] = useState('');
  const [touched, setTouched] = useState(false);
  const add = useRpcCommand('AddWarrantyClaimNoteCommand', {
    success: (row) => `Nota agregada al caso ${row.number}`,
    notifyError: false,
  });

  const claim = target ?? shown;
  if (!claim) return null;
  const problem = noteProblem(note);

  const change = (value: string) => {
    setNote(value);
    if (add.error) add.reset();
  };

  const submit = async () => {
    setTouched(true);
    if (problem) return;
    const outcome = await add.run({ number: claim.number, note: note.trim() });
    if (outcome.ok) {
      onAdded(outcome.result);
      onClose();
    }
  };

  return (
    <Dialog
      open={target !== null}
      onClose={onClose}
      dismissible={!add.sending}
      title={`Nota en el caso ${claim.number}`}
      description="La nota queda en la bitácora del caso con su fecha y usuario."
      footer={
        <>
          <Button variant="outline" onClick={onClose} disabled={add.sending}>
            Cancelar
          </Button>
          <Button type="submit" form={formId} leftIcon={<NotebookPen />} loading={add.sending}>
            Agregar nota
          </Button>
        </>
      }
    >
      <Form id={formId} onSubmit={submit} busy={add.sending} error={add.errorText && <ServerError text={add.errorText} details={add.error?.errors} />}>
        <TextArea label="Nota" value={note} onChange={change} maxLength={TEXT_MAX} rows={4} error={touched && problem ? problem : undefined} required data-autofocus />
        <div className="space-y-2">
          <p className="text-sm text-text-muted">Notas frecuentes:</p>
          <div className="flex flex-wrap gap-2">
            {NOTE_SUGGESTIONS.map((suggestion) => (
              <Button key={suggestion} variant="subtle" onClick={() => change(suggestion)}>
                {suggestion}
              </Button>
            ))}
          </div>
        </div>
      </Form>
    </Dialog>
  );
}
