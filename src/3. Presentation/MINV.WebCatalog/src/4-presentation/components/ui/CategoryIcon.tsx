import clsx from 'clsx';
import { Package } from 'lucide-react';
import { CATEGORY_ICONS } from './categoryIcons';

export interface CategoryIconProps {
  /** Nombre de `Category.icon` o `BuildSlot.icon` («Cpu», «Gpu»…). */
  name: string;
  className?: string;
  /** Por defecto es decorativo (aria-hidden). Pasá un `label` para que sea informativo. */
  label?: string;
}

/** Ícono de Lucide a partir del nombre que traen las categorías y las ranuras del dominio (Package si no existe). */
export function CategoryIcon({ name, className, label }: CategoryIconProps) {
  const Icon = CATEGORY_ICONS[name] ?? Package;
  return <Icon aria-hidden={label ? undefined : true} aria-label={label} role={label ? 'img' : undefined} className={clsx('size-6', className)} />;
}
