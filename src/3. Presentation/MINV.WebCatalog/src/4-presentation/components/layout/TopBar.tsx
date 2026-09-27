import { MapPin, MessageCircle, ShieldCheck, Truck } from 'lucide-react';
import { Container } from '@/4-presentation/components/ui/Container';
import { STORE } from '@/shared/constants';

/** Franja superior fina: envíos, garantía, WhatsApp y sucursales (datos de ejemplo). */
export function TopBar() {
  return (
    <div className="border-b border-border/60 bg-surface/70 text-xs text-text-muted">
      <Container className="flex h-9 items-center justify-between gap-4">
        <ul className="flex items-center gap-4">
          <li className="flex items-center gap-1.5">
            <Truck aria-hidden="true" className="size-4 text-accent" />
            <span>Envíos a todo el país</span>
          </li>
          <li className="hidden items-center gap-1.5 sm:flex">
            <ShieldCheck aria-hidden="true" className="size-4 text-accent" />
            <span>Garantía oficial</span>
          </li>
        </ul>
        <ul className="flex items-center gap-4">
          <li className="hidden md:block">
            <a
              href={STORE.whatsappUrl}
              target="_blank"
              rel="noreferrer noopener"
              className="-my-3 inline-flex items-center gap-1.5 py-3 font-medium text-text-muted transition-colors duration-200 hover:text-accent-hover"
            >
              <MessageCircle aria-hidden="true" className="size-4 text-success" />
              <span>WhatsApp {STORE.whatsapp}</span>
            </a>
          </li>
          <li className="flex items-center gap-1.5">
            <MapPin aria-hidden="true" className="size-4 text-accent" />
            <span className="hidden lg:inline">{STORE.branches.join(' · ')}</span>
            <span className="lg:hidden">{STORE.branches.length} sucursales</span>
          </li>
        </ul>
      </Container>
    </div>
  );
}
