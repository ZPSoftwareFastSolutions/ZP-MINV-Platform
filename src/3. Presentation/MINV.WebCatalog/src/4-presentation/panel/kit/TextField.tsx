// USO · Campos de texto del panel. Todos los campos del conjunto trabajan con el VALOR (no con el evento):
//
//   <TextField label="Nombre" value={nombre} onChange={setNombre} error={errores.nombre} required />
//   <TextField label="Correo" type="email" autoComplete="email" value={correo} onChange={setCorreo} optional />
//   <TextArea label="Motivo" value={motivo} onChange={setMotivo} maxLength={200} hint="Queda en la bitácora." />
//
// Alto de 44 px, etiqueta visible, ayuda y error enlazados. `TextArea` con `maxLength` muestra «12 / 200».

import clsx from 'clsx';
import type { InputHTMLAttributes, ReactNode, Ref, TextareaHTMLAttributes } from 'react';
import { Field, type FieldFrameProps } from './Field';
import { CONTROL } from './styles';

type NativeInput = Omit<InputHTMLAttributes<HTMLInputElement>, 'value' | 'defaultValue' | 'onChange' | 'id' | 'className' | 'children' | 'required'>;

export interface TextFieldProps extends FieldFrameProps, NativeInput {
  value: string;
  onChange: (value: string) => void;
  /** Ícono o texto fijo a la izquierda, dentro del campo. */
  leading?: ReactNode;
  /** Botón o texto a la derecha, dentro del campo. */
  trailing?: ReactNode;
  inputClassName?: string;
  ref?: Ref<HTMLInputElement>;
}

export function TextField({ label, hint, error, required, optional, hideLabel, className, value, onChange, leading, trailing, inputClassName, ref, type = 'text', ...input }: TextFieldProps) {
  return (
    <Field label={label} hint={hint} error={error} required={required} optional={optional} hideLabel={hideLabel} className={className}>
      {(control) => (
        <div className="relative">
          {leading && (
            <span aria-hidden="true" className="pointer-events-none absolute inset-y-0 left-0 flex items-center pl-3 text-sm text-text-muted [&_svg]:size-4">
              {leading}
            </span>
          )}
          <input
            ref={ref}
            type={type}
            value={value}
            onChange={(event) => onChange(event.target.value)}
            className={clsx(CONTROL, leading && 'pl-10', trailing && 'pr-12', inputClassName)}
            {...control}
            {...input}
          />
          {trailing && <div className="absolute inset-y-0 right-0 flex items-center">{trailing}</div>}
        </div>
      )}
    </Field>
  );
}

type NativeTextArea = Omit<TextareaHTMLAttributes<HTMLTextAreaElement>, 'value' | 'defaultValue' | 'onChange' | 'id' | 'className' | 'children' | 'required'>;

export interface TextAreaProps extends FieldFrameProps, NativeTextArea {
  value: string;
  onChange: (value: string) => void;
  ref?: Ref<HTMLTextAreaElement>;
}

export function TextArea({ label, hint, error, required, optional, hideLabel, className, value, onChange, maxLength, rows = 4, ref, ...textarea }: TextAreaProps) {
  const counter =
    maxLength != null ? (
      <span aria-hidden="true" className={clsx('text-xs tabular-nums', value.length >= maxLength ? 'text-warning-text' : 'text-text-faint')}>
        {value.length} / {maxLength}
      </span>
    ) : undefined;
  return (
    <Field label={label} hint={hint} error={error} required={required} optional={optional} hideLabel={hideLabel} labelExtra={counter} className={className}>
      {(control) => (
        <textarea
          ref={ref}
          rows={rows}
          maxLength={maxLength}
          value={value}
          onChange={(event) => onChange(event.target.value)}
          className={clsx(CONTROL, 'h-auto min-h-24 resize-y py-2.5 leading-relaxed')}
          {...control}
          {...textarea}
        />
      )}
    </Field>
  );
}
