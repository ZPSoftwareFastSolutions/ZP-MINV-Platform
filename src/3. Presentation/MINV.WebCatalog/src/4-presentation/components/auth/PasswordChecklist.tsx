// Indicador de los requisitos de la contraseña: cada uno se marca mientras la persona escribe. El estado no depende
// solo del color: cada requisito lleva su ícono y un texto para lectores de pantalla («cumplido» / «pendiente»).

import clsx from 'clsx';
import { Check, Circle } from 'lucide-react';
import { passwordRules } from '@/1-domain/auth/validation';

export interface PasswordChecklistProps {
  /** Id de la lista: el campo de la contraseña la referencia con `describedBy`. */
  id: string;
  password: string;
  className?: string;
}

export function PasswordChecklist({ id, password, className }: PasswordChecklistProps) {
  const rules = passwordRules(password);
  return (
    <div id={id} className={clsx('rounded-xl border border-border bg-surface-2 p-3', className)}>
      <p className="text-xs font-semibold uppercase tracking-wide text-text-faint">La contraseña debe tener</p>
      <ul className="mt-2 space-y-1.5 text-sm">
        {rules.map((rule) => (
          <li key={rule.key} data-met={rule.met} className={clsx('flex items-center gap-2', rule.met ? 'text-success-text' : 'text-text-muted')}>
            {rule.met ? <Check aria-hidden="true" className="size-4 shrink-0" /> : <Circle aria-hidden="true" className="size-4 shrink-0 text-text-faint" />}
            <span>{rule.label}</span>
            <span className="sr-only">{rule.met ? ': cumplido' : ': pendiente'}</span>
          </li>
        ))}
      </ul>
    </div>
  );
}
