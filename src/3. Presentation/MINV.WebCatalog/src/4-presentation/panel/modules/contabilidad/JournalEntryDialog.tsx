// Módulo «Contabilidad» · diálogo «Nuevo asiento» (`CreateJournalEntryCommand`; en el escritorio: AccountingView › Nuevo
// asiento). Plantillas (depósito, sueldos, alquiler, servicios, proveedores, aporte de capital, gastos), fecha,
// sucursal (si se ven varias), descripción y líneas con cuenta, Debe, Haber y glosa. Los totales y «cuadrado / no cuadra»
// se ven EN VIVO; la validación de la página es comodidad: la partida doble la decide el servidor (regla P-01).
// Un asiento registrado no se borra: antes de enviarlo se pide confirmación.

import { Plus, Save, Trash2 } from 'lucide-react';
import { useId, useMemo, useState } from 'react';
import { usePermissions, useRpcCommand } from '@/4-presentation/panel/hooks';
import { Alert, Button, ComboBox, ConfirmDialog, Dialog, Form, FormGrid, IconButton, MoneyField, SelectField, TextField, type ComboOption } from '@/4-presentation/panel/kit';
import { formatMoney, laPazToday } from '@/4-presentation/panel/lib';
import {
  ENTRY_DESCRIPTION_MAX,
  ENTRY_MEMO_MAX,
  ENTRY_TEMPLATES,
  accountLabel,
  balanceText,
  entryErrors,
  entryPayload,
  entryTotals,
  hasEntryErrors,
  postableAccounts,
  templatePrefill,
  type AccountRecord,
  type EntryLineDraft,
  type EntryPrefill,
} from './accounting';

export interface JournalEntryDialogProps {
  open: boolean;
  accounts: readonly AccountRecord[];
  /** Plantilla o asiento que se repite (null = en blanco). */
  prefill: EntryPrefill | null;
  onClose: () => void;
  onCreated: (number: string) => void;
}

/** Líneas del formulario a partir de lo que llega (al menos dos). */
function linesOf(prefill: EntryPrefill | null): EntryLineDraft[] {
  const lines = (prefill?.lines ?? []).map((line, index) => ({ id: index + 1, account: line.account, debit: line.debit, credit: line.credit, memo: line.memo }));
  while (lines.length < 2) lines.push({ id: lines.length + 1, account: '', debit: null, credit: null, memo: '' });
  return lines;
}

export function JournalEntryDialog({ open, accounts, prefill, onClose, onCreated }: JournalEntryDialogProps) {
  const formId = useId();
  const { session } = usePermissions();
  const [now] = useState(() => new Date());
  const today = laPazToday(now);
  const branches = session?.access.branches ?? [];
  const multiBranch = Boolean(session && (session.access.allBranches || branches.length > 1));

  const [template, setTemplate] = useState('');
  const [date, setDate] = useState(today);
  const [branchId, setBranchId] = useState(session?.access.activeBranchId ?? '');
  const [description, setDescription] = useState(prefill?.description ?? '');
  const [lines, setLines] = useState<EntryLineDraft[]>(() => linesOf(prefill));
  const [nextId, setNextId] = useState(() => linesOf(prefill).length + 1);
  const [touched, setTouched] = useState(false);
  const [confirming, setConfirming] = useState(false);

  const entry = useRpcCommand('CreateJournalEntryCommand', {
    success: (number, payload) => `Asiento ${number} registrado · ${payload.description}`,
    notifyError: false,
  });

  const options = useMemo<ComboOption[]>(() => postableAccounts(accounts).map((account) => ({ value: account.code, label: accountLabel(account) })), [accounts]);
  const errors = entryErrors({ date, description, lines }, now);
  const branchProblem = multiBranch && !branchId ? 'Elija la sucursal del asiento.' : undefined;
  const totals = entryTotals(lines);

  const changed = () => {
    if (entry.error) entry.reset();
  };
  const updateLine = (id: number, change: Partial<EntryLineDraft>) => {
    setLines((current) => current.map((line) => (line.id === id ? { ...line, ...change } : line)));
    changed();
  };
  const addLine = () => {
    setLines((current) => [...current, { id: nextId, account: '', debit: null, credit: null, memo: '' }]);
    setNextId(nextId + 1);
  };
  const removeLine = (id: number) => setLines((current) => (current.length > 2 ? current.filter((line) => line.id !== id) : current));
  const chooseTemplate = (value: string) => {
    setTemplate(value);
    const chosen = ENTRY_TEMPLATES.find((item) => item.id === value);
    const next = linesOf(chosen ? templatePrefill(chosen, accounts) : null).map((line, index) => ({ ...line, id: nextId + index }));
    setLines(next);
    setNextId(nextId + next.length);
    if (chosen) setDescription(chosen.description);
    changed();
  };

  const submit = () => {
    setTouched(true);
    if (hasEntryErrors(errors) || branchProblem) return;
    setConfirming(true);
  };
  const send = async () => {
    const outcome = await entry.run(entryPayload({ date, description, lines, branchId: multiBranch ? branchId || null : null }));
    if (outcome.ok) onCreated(outcome.result);
    return outcome;
  };

  const shown = touched ? errors : { lines: {} as ReturnType<typeof entryErrors>['lines'] };
  const branch = branches.find((item) => item.id === branchId);

  return (
    <>
      <Dialog
        open={open}
        onClose={onClose}
        dismissible={!entry.sending}
        size="xl"
        title="Nuevo asiento"
        description="Cada línea va al debe o al haber; el total del debe tiene que ser igual al del haber."
        footer={
          <>
            <Button variant="outline" onClick={onClose} disabled={entry.sending}>
              Cancelar
            </Button>
            <Button type="submit" form={formId} leftIcon={<Save />} loading={entry.sending}>
              Registrar asiento
            </Button>
          </>
        }
      >
        <Form id={formId} onSubmit={submit} error={entry.errorText} busy={entry.sending}>
          <FormGrid className="lg:grid-cols-3">
            <SelectField label="Plantilla" allLabel="Asiento en blanco" value={template} onChange={chooseTemplate} options={ENTRY_TEMPLATES.map((item) => ({ value: item.id, label: item.label }))} hint="Llena las cuentas; usted pone los montos." />
            <TextField
              label="Fecha"
              type="date"
              value={date}
              max={today}
              onChange={(value) => {
                setDate(value);
                changed();
              }}
              error={shown.date}
              required
            />
            {multiBranch && (
              <SelectField
                label="Sucursal"
                allLabel={false}
                placeholder="Elija la sucursal"
                value={branchId}
                onChange={(value) => {
                  setBranchId(value);
                  changed();
                }}
                options={branches.map((item) => ({ value: item.id, label: `${item.code} · ${item.name}` }))}
                error={touched ? branchProblem : undefined}
                required
              />
            )}
            <TextField
              label="Descripción"
              value={description}
              onChange={(value) => {
                setDescription(value);
                changed();
              }}
              error={shown.description}
              placeholder="Qué registra este asiento"
              maxLength={ENTRY_DESCRIPTION_MAX}
              className="sm:col-span-2 lg:col-span-3"
              required
            />
          </FormGrid>

          <div className="space-y-3">
            {lines.map((line, index) => {
              const problem = shown.lines[line.id];
              return (
                <fieldset key={line.id} className="min-w-0 rounded-card border border-border p-3" data-testid={`linea-asiento-${index + 1}`}>
                  <legend className="px-1 text-xs font-semibold uppercase tracking-wide text-text-muted">Línea {index + 1}</legend>
                  <div className="grid grid-cols-1 items-start gap-3 sm:grid-cols-2 lg:grid-cols-[minmax(0,2fr)_minmax(0,1fr)_minmax(0,1fr)_minmax(0,1.4fr)_auto]">
                    <ComboBox
                      label="Cuenta"
                      placeholder="Código o nombre"
                      value={options.find((option) => option.value === line.account) ?? (line.account ? { value: line.account, label: line.account } : null)}
                      onChange={(option) => updateLine(line.id, { account: option?.value ?? '' })}
                      options={options}
                      error={problem?.account}
                      className="sm:col-span-2 lg:col-span-1"
                    />
                    <MoneyField label="Debe" value={line.debit} onChange={(value) => updateLine(line.id, { debit: value })} error={problem?.amount} placeholder="0,00" />
                    <MoneyField label="Haber" value={line.credit} onChange={(value) => updateLine(line.id, { credit: value })} placeholder="0,00" />
                    <TextField label="Glosa" value={line.memo} onChange={(value) => updateLine(line.id, { memo: value })} error={problem?.memo} maxLength={ENTRY_MEMO_MAX} optional />
                    <div className="flex items-end lg:pt-6">
                      <IconButton label={`Quitar la línea ${index + 1}`} icon={<Trash2 />} onClick={() => removeLine(line.id)} disabled={lines.length <= 2} />
                    </div>
                  </div>
                </fieldset>
              );
            })}
            <Button variant="ghost" leftIcon={<Plus />} onClick={addLine}>
              Agregar línea
            </Button>
          </div>

          <div className="flex flex-col gap-2 rounded-card border border-border bg-surface-2/60 p-3 sm:flex-row sm:items-center sm:justify-between" data-testid="asiento-totales">
            <p role="status" aria-live="polite" className={`text-sm font-semibold ${totals.balanced ? 'text-success-text' : 'text-warning-text'}`}>
              {balanceText(lines)}
            </p>
            <p className="text-sm tabular-nums">
              Debe <span className="font-semibold">{formatMoney(totals.debit)}</span> · Haber <span className="font-semibold">{formatMoney(totals.credit)}</span>
            </p>
          </div>
          {touched && shown.general && <Alert tone="warning">{shown.general}</Alert>}
        </Form>
      </Dialog>

      <ConfirmDialog
        open={confirming}
        onClose={() => setConfirming(false)}
        title="¿Registrar el asiento?"
        message={`${description.trim()} · ${formatMoney(totals.debit)}${branch ? ` · sucursal ${branch.code}` : ''}. Un asiento registrado no se borra ni se edita: para corregirlo se registra otro asiento que lo compense.`}
        confirmLabel="Registrar asiento"
        onConfirm={send}
        error={entry.errorText}
      />
    </>
  );
}
