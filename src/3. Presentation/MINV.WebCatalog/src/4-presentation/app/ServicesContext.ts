import { createContext } from 'react';
import type { Services } from './container';

export const ServicesContext = createContext<Services | null>(null);
