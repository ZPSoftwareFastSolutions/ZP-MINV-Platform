import { LoaderCircle } from 'lucide-react';
import type { AnchorHTMLAttributes, ButtonHTMLAttributes, MouseEvent, ReactNode, Ref } from 'react';
import { Link, type LinkProps } from 'react-router-dom';
import { buttonClasses, type ButtonSize, type ButtonVariant } from './buttonStyles';

interface ButtonBaseProps {
  variant?: ButtonVariant;
  size?: ButtonSize;
  /** Muestra un indicador y bloquea el botón (aria-busy). */
  loading?: boolean;
  leftIcon?: ReactNode;
  rightIcon?: ReactNode;
  fullWidth?: boolean;
  className?: string;
  children?: ReactNode;
}

export type ButtonAsButtonProps = ButtonBaseProps &
  Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'className' | 'children'> & {
    to?: undefined;
    href?: undefined;
    /** Referencia al <button> (React 19: `ref` es una prop más). */
    ref?: Ref<HTMLButtonElement>;
  };

export type ButtonAsLinkProps = ButtonBaseProps &
  Omit<LinkProps, 'className' | 'children'> & { to: string; href?: undefined; disabled?: boolean; ref?: Ref<HTMLAnchorElement> };

export type ButtonAsAnchorProps = ButtonBaseProps &
  Omit<AnchorHTMLAttributes<HTMLAnchorElement>, 'className' | 'children' | 'href'> & {
    href: string;
    to?: undefined;
    disabled?: boolean;
    ref?: Ref<HTMLAnchorElement>;
  };

export type ButtonProps = ButtonAsButtonProps | ButtonAsLinkProps | ButtonAsAnchorProps;

function isLink(props: ButtonProps): props is ButtonAsLinkProps {
  return typeof (props as ButtonAsLinkProps).to === 'string';
}

function isAnchor(props: ButtonProps): props is ButtonAsAnchorProps {
  return typeof (props as ButtonAsAnchorProps).href === 'string';
}

function Content({ loading, leftIcon, rightIcon, children }: ButtonBaseProps) {
  return (
    <>
      {loading ? <LoaderCircle aria-hidden="true" className="animate-spin" /> : leftIcon}
      {children != null && <span className="min-w-0">{children}</span>}
      {!loading && rightIcon}
    </>
  );
}

/**
 * Botón del sitio. Con `to` se convierte en enlace del enrutador; con `href`, en enlace externo.
 * Variantes: primary (violeta) · brand (violeta con halo) · accent (cian) · cta (rosa, ofertas) · danger (rojo,
 * confirmaciones destructivas) · outline · ghost · subtle. Ver el criterio de uso en buttonStyles.ts.
 */
export function Button(props: ButtonProps) {
  if (isLink(props)) {
    const { variant, size, loading, leftIcon, rightIcon, fullWidth, className, children, to, disabled, onClick, ref, ...rest } = props;
    const blocked = disabled || loading;
    return (
      <Link
        ref={ref}
        to={to}
        aria-disabled={blocked || undefined}
        tabIndex={blocked ? -1 : undefined}
        className={buttonClasses({ variant, size, fullWidth, className })}
        onClick={(event: MouseEvent<HTMLAnchorElement>) => {
          if (blocked) {
            event.preventDefault();
            return;
          }
          onClick?.(event);
        }}
        {...rest}
      >
        <Content loading={loading} leftIcon={leftIcon} rightIcon={rightIcon}>
          {children}
        </Content>
      </Link>
    );
  }
  if (isAnchor(props)) {
    const { variant, size, loading, leftIcon, rightIcon, fullWidth, className, children, href, disabled, ref, ...rest } = props;
    return (
      <a
        ref={ref}
        href={disabled ? undefined : href}
        aria-disabled={disabled || undefined}
        className={buttonClasses({ variant, size, fullWidth, className })}
        {...rest}
      >
        <Content loading={loading} leftIcon={leftIcon} rightIcon={rightIcon}>
          {children}
        </Content>
      </a>
    );
  }
  const { variant, size, loading, leftIcon, rightIcon, fullWidth, className, children, type = 'button', disabled, ref, ...rest } = props;
  return (
    <button
      ref={ref}
      type={type}
      disabled={disabled || loading}
      aria-busy={loading || undefined}
      className={buttonClasses({ variant, size, fullWidth, className })}
      {...rest}
    >
      <Content loading={loading} leftIcon={leftIcon} rightIcon={rightIcon}>
        {children}
      </Content>
    </button>
  );
}
