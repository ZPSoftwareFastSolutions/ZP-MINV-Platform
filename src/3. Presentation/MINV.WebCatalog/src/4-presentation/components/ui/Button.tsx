import { LoaderCircle } from 'lucide-react';
import type { AnchorHTMLAttributes, ButtonHTMLAttributes, MouseEvent, ReactNode } from 'react';
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
  Omit<ButtonHTMLAttributes<HTMLButtonElement>, 'className' | 'children'> & { to?: undefined; href?: undefined };

export type ButtonAsLinkProps = ButtonBaseProps &
  Omit<LinkProps, 'className' | 'children'> & { to: string; href?: undefined; disabled?: boolean };

export type ButtonAsAnchorProps = ButtonBaseProps &
  Omit<AnchorHTMLAttributes<HTMLAnchorElement>, 'className' | 'children' | 'href'> & {
    href: string;
    to?: undefined;
    disabled?: boolean;
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
      {children != null && <span>{children}</span>}
      {!loading && rightIcon}
    </>
  );
}

/**
 * Botón del sitio. Con `to` se convierte en enlace del enrutador; con `href`, en enlace externo.
 * Variantes: primary (violeta) · brand (violeta con halo) · accent (cian) · cta (rosa, ofertas) · outline · ghost · subtle.
 */
export function Button(props: ButtonProps) {
  if (isLink(props)) {
    const { variant, size, loading, leftIcon, rightIcon, fullWidth, className, children, to, disabled, onClick, ...rest } = props;
    const blocked = disabled || loading;
    return (
      <Link
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
    const { variant, size, loading, leftIcon, rightIcon, fullWidth, className, children, href, disabled, ...rest } = props;
    return (
      <a
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
  const { variant, size, loading, leftIcon, rightIcon, fullWidth, className, children, type = 'button', disabled, ...rest } = props;
  return (
    <button
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
