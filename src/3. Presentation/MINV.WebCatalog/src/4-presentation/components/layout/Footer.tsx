import { AtSign, Banknote, Clock, CreditCard, Globe, Landmark, Mail, MapPin, MessageCircle, Phone, QrCode, ShieldCheck, Truck } from 'lucide-react';
import { Link } from 'react-router-dom';
import { ROUTES } from '@/4-presentation/app/routes';
import { Container } from '@/4-presentation/components/ui/Container';
import { useServices } from '@/4-presentation/hooks/useServices';
import { useStore } from '@/4-presentation/hooks/useStore';
import { STORE } from '@/shared/constants';
import { Logo } from './Logo';

const LINK = 'inline-flex min-h-9 items-center gap-2 text-sm text-text-muted transition-colors duration-200 hover:text-accent-hover';

const PAYMENTS = [
  { icon: QrCode, label: 'Pago con QR' },
  { icon: CreditCard, label: 'Tarjeta de crédito y débito' },
  { icon: Landmark, label: 'Transferencia bancaria' },
  { icon: Banknote, label: 'Efectivo en sucursal' },
] as const;

const SOCIAL = [
  { label: 'Facebook', href: 'https://facebook.example/techzone' },
  { label: 'Instagram', href: 'https://instagram.example/techzone' },
  { label: 'TikTok', href: 'https://tiktok.example/@techzone' },
] as const;

export function Footer() {
  const { catalog, generatedAt } = useServices();
  const { company, branch } = useStore();
  const roots = catalog.getRootCategories();
  const branches = company.branches.map((item) => item.name).join(' · ');

  return (
    <footer className="mt-16 border-t border-border bg-surface/60">
      <Container className="py-12">
        <div className="grid grid-cols-1 gap-10 sm:grid-cols-2 lg:grid-cols-4">
          <div>
            <Logo />
            <p className="mt-4 max-w-xs text-sm text-text-muted">{STORE.tagline}</p>
            <ul className="mt-4 space-y-2 text-sm text-text-muted">
              <li className="flex items-start gap-2">
                <MapPin aria-hidden="true" className="mt-0.5 size-4 shrink-0 text-accent" />
                <span>
                  {STORE.address}
                  <br />
                  {STORE.city}
                </span>
              </li>
              <li className="flex items-start gap-2">
                <Clock aria-hidden="true" className="mt-0.5 size-4 shrink-0 text-accent" />
                <span>{STORE.hours}</span>
              </li>
              <li className="flex items-start gap-2">
                <Globe aria-hidden="true" className="mt-0.5 size-4 shrink-0 text-accent" />
                <span>{company.branches.length > 1 ? `Sucursales: ${branches}` : branch.name}</span>
              </li>
            </ul>
          </div>

          <nav aria-labelledby="footer-categorias">
            <h2 id="footer-categorias" className="font-display text-base font-semibold text-text">
              Categorías
            </h2>
            <ul className="mt-3 grid grid-cols-1 gap-y-0.5">
              {roots.map((category) => (
                <li key={category.code}>
                  <Link to={ROUTES.category(category.slug)} className={LINK}>
                    {category.name}
                  </Link>
                </li>
              ))}
              <li>
                <Link to={ROUTES.builder} className={LINK + ' font-semibold text-accent'}>
                  Armá tu PC
                </Link>
              </li>
              <li>
                <Link to={ROUTES.reservations} className={LINK + ' font-semibold text-accent'}>
                  Consultar mi reserva
                </Link>
              </li>
            </ul>
          </nav>

          <div>
            <h2 className="font-display text-base font-semibold text-text">Ayuda</h2>
            <ul className="mt-3 space-y-3 text-sm text-text-muted">
              <li className="flex items-start gap-2">
                <Truck aria-hidden="true" className="mt-0.5 size-4 shrink-0 text-accent" />
                <span>
                  <span className="font-medium text-text">Reservas</span> desde el armador: te guardamos las piezas 48 horas en {branch.name} y las
                  confirmás y pagás en la tienda.
                </span>
              </li>
              <li className="flex items-start gap-2">
                <ShieldCheck aria-hidden="true" className="mt-0.5 size-4 shrink-0 text-accent" />
                <span>
                  <span className="font-medium text-text">Garantías</span> oficiales de 12 a 36 meses según el producto, con servicio técnico propio.
                </span>
              </li>
              <li className="flex items-start gap-2">
                <QrCode aria-hidden="true" className="mt-0.5 size-4 shrink-0 text-accent" />
                <span>
                  <span className="font-medium text-text">Pagos</span> con QR, tarjeta o transferencia; cuotas con tarjetas de bancos asociados.
                </span>
              </li>
            </ul>
          </div>

          <div>
            <h2 className="font-display text-base font-semibold text-text">Contacto</h2>
            <ul className="mt-3 space-y-1">
              <li>
                <a href={STORE.whatsappUrl} target="_blank" rel="noreferrer noopener" className={LINK}>
                  <MessageCircle aria-hidden="true" className="size-4 text-success" />
                  WhatsApp {STORE.whatsapp}
                </a>
              </li>
              <li>
                <a href={`tel:${STORE.phone.replace(/\s+/g, '')}`} className={LINK}>
                  <Phone aria-hidden="true" className="size-4 text-accent" />
                  {STORE.phone}
                </a>
              </li>
              <li>
                <a href={`mailto:${STORE.email}`} className={LINK}>
                  <Mail aria-hidden="true" className="size-4 text-accent" />
                  {STORE.email}
                </a>
              </li>
            </ul>
            <p className="mt-4 text-xs font-semibold uppercase tracking-[0.2em] text-text-faint">Redes</p>
            <ul className="mt-1 flex flex-wrap gap-x-4">
              {SOCIAL.map((social) => (
                <li key={social.label}>
                  <a href={social.href} target="_blank" rel="noreferrer noopener" className={LINK}>
                    <AtSign aria-hidden="true" className="size-4 text-accent" />
                    {social.label}
                  </a>
                </li>
              ))}
            </ul>
          </div>
        </div>

        <div className="mt-10 flex flex-col gap-4 border-t border-border pt-6 sm:flex-row sm:items-center sm:justify-between">
          <ul className="flex flex-wrap gap-x-5 gap-y-2" aria-label="Medios de pago">
            {PAYMENTS.map((payment) => (
              <li key={payment.label} className="inline-flex items-center gap-2 text-xs text-text-muted">
                <payment.icon aria-hidden="true" className="size-5 text-text-faint" />
                {payment.label}
              </li>
            ))}
          </ul>
        </div>
        <p className="mt-6 text-xs text-text-faint">
          © {STORE.year} {company.name} · Precios en Bs con IVA incluido y disponibilidad de {branch.name}, actualizados a las{' '}
          {new Intl.DateTimeFormat('es-BO', { hour: '2-digit', minute: '2-digit' }).format(generatedAt)}.
        </p>
      </Container>
    </footer>
  );
}
