// Archivo GENERADO por tools/generar_catalogo_web.py a partir del catálogo de tecnología de la V4.2. No editar a mano.
import type { Category } from '@/1-domain/catalog/types';

export const CATEGORIES: Category[] = [
  {
    "code": "COMP",
    "name": "Componentes",
    "slug": "componentes",
    "parent": null,
    "icon": "Cpu",
    "description": "Componentes para armar o actualizar tu PC",
    "productCount": 63
  },
  {
    "code": "CPU",
    "name": "Procesadores",
    "slug": "procesadores",
    "parent": "COMP",
    "icon": "Cpu",
    "description": "Componentes > Procesadores",
    "productCount": 10
  },
  {
    "code": "GPU",
    "name": "Tarjetas de video",
    "slug": "tarjetas-de-video",
    "parent": "COMP",
    "icon": "Gpu",
    "description": "Componentes > Tarjetas de video",
    "productCount": 12
  },
  {
    "code": "MB",
    "name": "Placas madre",
    "slug": "placas-madre",
    "parent": "COMP",
    "icon": "CircuitBoard",
    "description": "Componentes > Placas madre",
    "productCount": 9
  },
  {
    "code": "RAM",
    "name": "Memorias RAM",
    "slug": "memorias-ram",
    "parent": "COMP",
    "icon": "MemoryStick",
    "description": "Componentes > Memorias RAM",
    "productCount": 6
  },
  {
    "code": "STO",
    "name": "Almacenamiento",
    "slug": "almacenamiento",
    "parent": "COMP",
    "icon": "HardDrive",
    "description": "Componentes > Almacenamiento",
    "productCount": 8
  },
  {
    "code": "PSU",
    "name": "Fuentes de poder",
    "slug": "fuentes-de-poder",
    "parent": "COMP",
    "icon": "Zap",
    "description": "Componentes > Fuentes de poder",
    "productCount": 6
  },
  {
    "code": "CASE",
    "name": "Gabinetes",
    "slug": "gabinetes",
    "parent": "COMP",
    "icon": "Box",
    "description": "Componentes > Gabinetes",
    "productCount": 6
  },
  {
    "code": "COOL",
    "name": "Refrigeración",
    "slug": "refrigeracion",
    "parent": "COMP",
    "icon": "Fan",
    "description": "Componentes > Refrigeración",
    "productCount": 6
  },
  {
    "code": "PC",
    "name": "Computadoras",
    "slug": "computadoras",
    "parent": null,
    "icon": "MonitorSmartphone",
    "description": "Computadoras de escritorio y portátiles listas para usar",
    "productCount": 12
  },
  {
    "code": "LAPG",
    "name": "Laptops gamer",
    "slug": "laptops-gamer",
    "parent": "PC",
    "icon": "Laptop",
    "description": "Computadoras > Laptops gamer",
    "productCount": 7
  },
  {
    "code": "LAPU",
    "name": "Laptops y ultrabooks",
    "slug": "laptops-y-ultrabooks",
    "parent": "PC",
    "icon": "Laptop",
    "description": "Computadoras > Laptops y ultrabooks",
    "productCount": 3
  },
  {
    "code": "DESK",
    "name": "PC de escritorio",
    "slug": "pc-de-escritorio",
    "parent": "PC",
    "icon": "PcCase",
    "description": "Computadoras > PC de escritorio",
    "productCount": 2
  },
  {
    "code": "MON",
    "name": "Monitores",
    "slug": "monitores",
    "parent": null,
    "icon": "Monitor",
    "description": "Monitores gaming y profesionales",
    "productCount": 9
  },
  {
    "code": "PER",
    "name": "Periféricos",
    "slug": "perifericos",
    "parent": null,
    "icon": "Keyboard",
    "description": "Teclados, mouse, audio y todo lo que va en tu escritorio",
    "productCount": 19
  },
  {
    "code": "KEY",
    "name": "Teclados",
    "slug": "teclados",
    "parent": "PER",
    "icon": "Keyboard",
    "description": "Periféricos > Teclados",
    "productCount": 5
  },
  {
    "code": "MOU",
    "name": "Mouse",
    "slug": "mouse",
    "parent": "PER",
    "icon": "Mouse",
    "description": "Periféricos > Mouse",
    "productCount": 5
  },
  {
    "code": "AUD",
    "name": "Audífonos y headsets",
    "slug": "audifonos-y-headsets",
    "parent": "PER",
    "icon": "Headphones",
    "description": "Periféricos > Audífonos y headsets",
    "productCount": 4
  },
  {
    "code": "PAD",
    "name": "Mousepads",
    "slug": "mousepads",
    "parent": "PER",
    "icon": "Square",
    "description": "Periféricos > Mousepads",
    "productCount": 2
  },
  {
    "code": "CAM",
    "name": "Webcams y micrófonos",
    "slug": "webcams-y-microfonos",
    "parent": "PER",
    "icon": "Webcam",
    "description": "Periféricos > Webcams y micrófonos",
    "productCount": 2
  },
  {
    "code": "CHA",
    "name": "Sillas gamer",
    "slug": "sillas-gamer",
    "parent": "PER",
    "icon": "Armchair",
    "description": "Periféricos > Sillas gamer",
    "productCount": 1
  },
  {
    "code": "CON",
    "name": "Consolas",
    "slug": "consolas",
    "parent": null,
    "icon": "Gamepad2",
    "description": "PlayStation, Xbox y Nintendo",
    "productCount": 11
  },
  {
    "code": "CPS",
    "name": "PlayStation",
    "slug": "playstation",
    "parent": "CON",
    "icon": "Gamepad2",
    "description": "Consolas > PlayStation",
    "productCount": 4
  },
  {
    "code": "CXB",
    "name": "Xbox",
    "slug": "xbox",
    "parent": "CON",
    "icon": "Gamepad2",
    "description": "Consolas > Xbox",
    "productCount": 3
  },
  {
    "code": "CNS",
    "name": "Nintendo",
    "slug": "nintendo",
    "parent": "CON",
    "icon": "Gamepad2",
    "description": "Consolas > Nintendo",
    "productCount": 4
  },
  {
    "code": "JUE",
    "name": "Videojuegos",
    "slug": "videojuegos",
    "parent": null,
    "icon": "Disc3",
    "description": "Videojuegos físicos y digitales",
    "productCount": 15
  },
  {
    "code": "ACC",
    "name": "Accesorios de consola",
    "slug": "accesorios-de-consola",
    "parent": null,
    "icon": "Plug",
    "description": "Mandos, cargadores y almacenamiento para consola",
    "productCount": 12
  },
  {
    "code": "MAND",
    "name": "Mandos",
    "slug": "mandos",
    "parent": "ACC",
    "icon": "Gamepad",
    "description": "Accesorios de consola > Mandos",
    "productCount": 7
  },
  {
    "code": "CARG",
    "name": "Cargadores y energía",
    "slug": "cargadores-y-energia",
    "parent": "ACC",
    "icon": "BatteryCharging",
    "description": "Accesorios de consola > Cargadores y energía",
    "productCount": 1
  },
  {
    "code": "ALMC",
    "name": "Almacenamiento para consola",
    "slug": "almacenamiento-para-consola",
    "parent": "ACC",
    "icon": "HardDrive",
    "description": "Accesorios de consola > Almacenamiento para consola",
    "productCount": 4
  },
  {
    "code": "RED",
    "name": "Redes",
    "slug": "redes",
    "parent": null,
    "icon": "Router",
    "description": "Routers y conectividad",
    "productCount": 5
  },
  {
    "code": "CAB",
    "name": "Cables y adaptadores",
    "slug": "cables-y-adaptadores",
    "parent": null,
    "icon": "Cable",
    "description": "Cables y adaptadores",
    "productCount": 4
  },
  {
    "code": "SOFT",
    "name": "Software y servicios",
    "slug": "software-y-servicios",
    "parent": null,
    "icon": "Package",
    "description": "Licencias y servicios técnicos",
    "productCount": 9
  },
  {
    "code": "LIC",
    "name": "Licencias de software",
    "slug": "licencias-de-software",
    "parent": "SOFT",
    "icon": "KeyRound",
    "description": "Software y servicios > Licencias de software",
    "productCount": 5
  },
  {
    "code": "SRV",
    "name": "Servicios técnicos",
    "slug": "servicios-tecnicos",
    "parent": "SOFT",
    "icon": "Wrench",
    "description": "Software y servicios > Servicios técnicos",
    "productCount": 4
  }
];
