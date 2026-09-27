import { RouterProvider } from 'react-router-dom';
import { ToastProvider } from '@/4-presentation/components/feedback/ToastProvider';
import { BuilderProvider } from '@/4-presentation/state/BuilderProvider';
import { services } from './container';
import { router } from './router';
import { ServicesProvider } from './ServicesProvider';

/** Composición de la aplicación: servicios (mock) → avisos → armado en memoria → enrutador. */
export function App() {
  return (
    <ServicesProvider services={services}>
      <ToastProvider>
        <BuilderProvider>
          <RouterProvider router={router} />
        </BuilderProvider>
      </ToastProvider>
    </ServicesProvider>
  );
}
