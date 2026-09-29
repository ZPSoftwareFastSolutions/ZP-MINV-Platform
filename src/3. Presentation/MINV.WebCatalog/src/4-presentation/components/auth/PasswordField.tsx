// Campo de contraseña con el botón «Mostrar / Ocultar». La contraseña vive solo en el estado del formulario mientras se
// escribe: no se guarda, no se registra y no viaja en la URL (regla P-03).

import { Eye, EyeOff } from 'lucide-react';
import { useState } from 'react';
import { TextField, type TextFieldProps } from '@/4-presentation/components/ui/TextField';

export interface PasswordFieldProps extends Omit<TextFieldProps, 'type' | 'trailing'> {
  /** `current-password` al ingresar; `new-password` al crear o cambiar la contraseña. */
  autoComplete: 'current-password' | 'new-password';
  /** Cómo nombra el botón a este campo («la contraseña actual»): distingue los botones cuando hay varios campos. */
  revealName?: string;
}

export function PasswordField({ revealName = 'la contraseña', disabled, ...props }: PasswordFieldProps) {
  const [visible, setVisible] = useState(false);
  const action = `${visible ? 'Ocultar' : 'Mostrar'} ${revealName}`;
  return (
    <TextField
      {...props}
      disabled={disabled}
      type={visible ? 'text' : 'password'}
      autoCapitalize="none"
      autoCorrect="off"
      spellCheck={false}
      trailing={
        <button
          type="button"
          aria-pressed={visible}
          aria-label={action}
          title={action}
          disabled={disabled}
          onClick={() => setVisible((current) => !current)}
          className="flex size-11 cursor-pointer items-center justify-center rounded-xl text-text-muted transition-colors duration-200 hover:text-text disabled:cursor-not-allowed disabled:opacity-50"
        >
          {visible ? <EyeOff aria-hidden="true" className="size-5" /> : <Eye aria-hidden="true" className="size-5" />}
        </button>
      }
    />
  );
}
