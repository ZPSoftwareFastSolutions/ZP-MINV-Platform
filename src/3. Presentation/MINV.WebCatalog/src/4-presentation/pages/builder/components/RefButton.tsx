// Botón con el mismo aspecto del kit (buttonClasses) pero con referencia al <button>: el armador mueve el foco entre
// controles que aparecen y desaparecen (confirmaciones inline, barra de móvil) y el <Button> del kit no expone su ref.

import type { ButtonHTMLAttributes, ReactNode, RefObject } from 'react';
import { buttonClasses, type ButtonSize, type ButtonVariant } from '@/4-presentation/components/ui/buttonStyles';

export interface RefButtonProps extends Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'className' | 'children'> {
  buttonRef?: RefObject<HTMLButtonElement | null>;
  variant?: ButtonVariant;
  size?: ButtonSize;
  leftIcon?: ReactNode;
  rightIcon?: ReactNode;
  fullWidth?: boolean;
  className?: string;
  children?: ReactNode;
}

export function RefButton({ buttonRef, variant, size, leftIcon, rightIcon, fullWidth, className, children, type = 'button', ...rest }: RefButtonProps) {
  return (
    <button ref={buttonRef} type={type} className={buttonClasses({ variant, size, fullWidth, className })} {...rest}>
      {leftIcon}
      {children != null && <span>{children}</span>}
      {rightIcon}
    </button>
  );
}
