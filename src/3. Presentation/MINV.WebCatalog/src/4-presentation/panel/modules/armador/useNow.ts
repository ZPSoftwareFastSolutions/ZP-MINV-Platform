// Módulo «Armador de PC» · la hora de ahora, que se renueva sola cada minuto: el estado «Reserva vencida» y la nota del
// armado abierto cambian mientras la pantalla está abierta, sin recargar.

import { useEffect, useState } from 'react';

export const NOW_REFRESH_MS = 60_000;

export function useNow(intervalMs: number = NOW_REFRESH_MS): Date {
  const [now, setNow] = useState(() => new Date());
  useEffect(() => {
    const timer = setInterval(() => setNow(new Date()), intervalMs);
    return () => clearInterval(timer);
  }, [intervalMs]);
  return now;
}
