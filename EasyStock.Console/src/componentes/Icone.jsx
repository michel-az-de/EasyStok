import {
  Archive, ArrowLeft, ArrowLeftRight, ArrowUp, ArrowUpDown, Ban, Bell, BellPlus, Bike, BookOpen,
  Camera, Check, ChevronDown, ChevronRight, ChevronUp, CircleCheck, CircleQuestionMark, CircleX,
  Clock, CookingPot, Copy, CreditCard, DollarSign, Download, EllipsisVertical, ExternalLink,
  FlaskConical, Frown, Gift, Globe, GripVertical, Hand, History, Hourglass, House, Image, Inbox,
  LayoutDashboard, LayoutTemplate, Lightbulb, Link, ListFilter, Lock, LogOut, Mail, MapPin,
  Maximize2, Meh, MessageSquareText, MessageSquareWarning, Mic, Minimize2, Minus, Moon, Package,
  PanelLeft, PanelRight, Paperclip, Pause, Pencil, Phone, PictureInPicture2, Play, Plus, Printer,
  RefreshCw, Search, SendHorizontal, Settings, ShoppingCart, Smile, Sparkles, Star, Sticker,
  StickyNote, Sun, Thermometer, ThumbsDown, ThumbsUp, Trash, TriangleAlert, Undo2, UserRound,
  UserRoundPlus, X, Zap,
  createLucideIcon,
} from 'lucide-react'
import css from './Icone.module.css'

// Fachada fina sobre o `lucide-react` (#2145, plano balcao-ux seção 3.2): quem chama
// continua pedindo `<Icone nome="..." />` e este arquivo traduz a chave para o
// componente do Lucide. Cada ícone entra por import nomeado, então o build leva só os
// desenhos listados aqui; o pacote é empacotado no bundle, sem CDN (a política de
// conteúdo do host bloqueia imagem de fora). Traço de 1,75 no grid de 24, pontas e
// junções arredondadas, e tamanho em `em` para o ícone crescer junto com o texto
// quando a pessoa aumenta a fonte do sistema.
//
// Uma chave, um sentido: apelidos antigos (`lupa`/`search`, `fechar`/`x`,
// `mais`/`plus`, `panela`/`cooking-pot`, `pega`/`grip-vertical`) apontam para o mesmo
// ícone. Chave nova só entra aqui; `ferramentas/verificar-gramatica.mjs` reprova
// chave usada no código que não esteja neste dicionário.

// Traço próprio, só para o que o Lucide não tem: logotipos de canal. O Lucide 1.x
// retirou os ícones de marca, e o cliente reconhece o canal pelo glifo. Passam pelo
// mesmo `createLucideIcon` para herdar traço, pontas e atributos dos demais.
const proprio = (nome, d) => createLucideIcon(nome, [['path', { d, key: nome }]])

const PROPRIOS = {
  whatsapp: proprio('whatsapp', 'M3 21l1.7-4.9A9 9 0 1 1 8 19.6L3 21ZM9 8.5c0 3.3 3.2 6.5 6.5 6.5l1.5-1.5-2-1.2-1 1a4.5 4.5 0 0 1-2.8-2.8l1-1-1.2-2L9 8.5Z'),
  instagram: proprio('instagram', 'M7 3h10a4 4 0 0 1 4 4v10a4 4 0 0 1-4 4H7a4 4 0 0 1-4-4V7a4 4 0 0 1 4-4ZM12 16a4 4 0 1 0 0-8 4 4 0 0 0 0 8ZM17.5 6.5h.01'),
}

const ICONES = {
  // Conceitos da tabela 3.2 do plano: a chave antiga passa a desenhar o ícone do conceito.
  automacao: Settings, settings: Settings,
  lupa: Search, search: Search, busca: Search,
  'list-filter': ListFilter,
  ellipsis: EllipsisVertical, 'ellipsis-vertical': EllipsisVertical,
  'arrow-left': ArrowLeft,
  fechar: X, x: X,
  anexo: Paperclip,
  enviar: SendHorizontal,
  mic: Mic,
  archive: Archive,
  'user-plus': UserRoundPlus,
  nota: StickyNote,
  ficha: UserRound,
  cardapio: BookOpen,
  moto: Bike,
  'circle-check': CircleCheck,
  'log-out': LogOut,
  relogio: Clock,
  agente: Sparkles, sparkles: Sparkles,
  ajuda: CircleQuestionMark,

  // Atendimento e conversa.
  balcao: Inbox, inbox: Inbox,
  conversa: MessageSquareText, respostas: MessageSquareText,
  'message-square-warning': MessageSquareWarning,
  raio: Zap,
  modelo: LayoutTemplate,
  imagem: Image,
  figurinha: Sticker,
  camera: Camera,
  email: Mail,
  phone: Phone,
  link: Link,
  copy: Copy,
  hand: Hand,
  dica: Lightbulb,
  termometro: Thermometer,
  'thumbs-up': ThumbsUp, 'thumbs-down': ThumbsDown,
  smile: Smile, meh: Meh, frown: Frown,
  globo: Globe,

  // Pedido, cobrança, cozinha e entrega.
  'dollar-sign': DollarSign,
  'credit-card': CreditCard,
  'shopping-cart': ShoppingCart,
  package: Package,
  panela: CookingPot, 'cooking-pot': CookingPot,
  house: House,
  'map-pin': MapPin,
  printer: Printer,
  'flask-conical': FlaskConical,
  hourglass: Hourglass,
  historico: History,

  // Estado e confirmação.
  check: Check,
  alerta: TriangleAlert,
  bloqueio: Ban,
  lock: Lock,
  'circle-x': CircleX,
  bell: Bell, 'bell-plus': BellPlus,
  estrela: Star,
  presente: Gift,

  // Ações genéricas.
  mais: Plus, plus: Plus,
  menos: Minus,
  lapis: Pencil,
  lixeira: Trash,
  'undo-2': Undo2,
  'refresh-cw': RefreshCw,
  download: Download,
  'external-link': ExternalLink,
  play: Play, pause: Pause,
  sun: Sun, moon: Moon,

  // Navegação e ordem.
  'arrow-up': ArrowUp,
  'arrow-up-down': ArrowUpDown,
  'chevron-up': ChevronUp, 'chevron-down': ChevronDown, 'chevron-right': ChevronRight,
  painel: LayoutDashboard,

  // Painéis reposicionáveis do Balcão (#1806).
  ampliar: Maximize2,
  reduzir: Minimize2,
  trocarLado: ArrowLeftRight,
  encaixarEsquerda: PanelLeft,
  encaixarDireita: PanelRight,
  solto: PictureInPicture2,
  pega: GripVertical, 'grip-vertical': GripVertical,

  ...PROPRIOS,
}

export function Icone({ nome, tamanho = '1.15em', rotulo, className }) {
  const Desenho = ICONES[nome]
  if (!Desenho) return null
  // `tamanho` aceita número em px (secao 10 da direção visual), além do `em`
  // de sempre para crescer com a fonte do sistema.
  const medida = typeof tamanho === 'number' ? `${tamanho}px` : tamanho
  return (
    <Desenho
      className={className ? `${css.icone} ${className}` : css.icone}
      size={medida}
      strokeWidth={1.75}
      role={rotulo ? 'img' : 'presentation'}
      aria-label={rotulo}
      aria-hidden={rotulo ? undefined : 'true'}
    />
  )
}
