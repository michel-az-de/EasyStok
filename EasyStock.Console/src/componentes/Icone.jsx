import css from './Icone.module.css'

// Conjunto de ícones desenhado no espírito do SF Symbols da Apple: traço único
// de 1,75 no grid de 24, pontas e junções arredondadas, e tamanho em `em` para
// o ícone crescer junto com o texto quando a pessoa aumenta a fonte do sistema.
// Nenhuma biblioteca externa: são poucos glifos e a política de conteúdo do
// host bloqueia CDN de imagem.

const TRACOS = {
  balcao: 'M3 7h18M5 7v12a1 1 0 0 0 1 1h12a1 1 0 0 0 1-1V7M8 7V5a1 1 0 0 1 1-1h6a1 1 0 0 1 1 1v2M9 12h6',
  conversa: 'M4 5h16a1 1 0 0 1 1 1v9a1 1 0 0 1-1 1H9l-5 4V6a1 1 0 0 1 1-1ZM8 9h8M8 12h5',
  ficha: 'M7 3h10a1 1 0 0 1 1 1v16l-6-3-6 3V4a1 1 0 0 1 1-1ZM9 8h6M9 11h4',
  enviar: 'M4 12 20 5l-7 15-2-6-7-2Z',
  raio: 'M13 3 5 13h5l-1 8 8-10h-5l1-8Z',
  nota: 'M5 4h11l3 3v13H5V4ZM16 4v3h3M8 11h8M8 14h6M8 17h4',
  modelo: 'M4 5h16v4H4zM4 12h7v7H4zM14 12h6M14 15h6M14 18h4',
  agente: 'M12 3v3M7.5 6.5 9 8M16.5 6.5 15 8M5 11h14a1 1 0 0 1 1 1v6a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1v-6a1 1 0 0 1 1-1ZM9 15h.01M15 15h.01',
  relogio: 'M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18ZM12 7v5l3 2',
  imagem: 'M4 5h16a1 1 0 0 1 1 1v12a1 1 0 0 1-1 1H4a1 1 0 0 1-1-1V6a1 1 0 0 1 1-1ZM8 11a1.5 1.5 0 1 0 0-3 1.5 1.5 0 0 0 0 3ZM3 16l5-4 4 3 3-2 6 4',
  figurinha: 'M12 3a9 9 0 0 1 9 9c0 .4-.03.8-.08 1.2L13.2 20.9c-.4.06-.8.1-1.2.1a9 9 0 1 1 0-18ZM9 10h.01M15 10h.01M9 14.5s1 1.5 3 1.5 3-1.5 3-1.5M13 21c0-4 3-7 8-7',
  cardapio: 'M5 4h14v16H5zM9 8h6M9 12h6M9 16h3',
  panela: 'M3 10h18M5 10v6a3 3 0 0 0 3 3h8a3 3 0 0 0 3-3v-6M19 12h2M9 6c0-1.5 3-1.5 3-3M14 6c0-1 2-1 2-2.5',
  moto: 'M6 18a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5ZM18 18a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5ZM8.5 15.5h7M15 7h3l1.5 6M7 7h5l3.5 8',
  historico: 'M3 12a9 9 0 1 0 3-6.7M3 4v4h4M12 8v4.5l3 1.8',
  automacao: 'M4 6h8M16 6h4M4 12h4M12 12h8M4 18h10M18 18h2M14 4v4M10 10v4M16 16v4',
  lupa: 'M11 4a7 7 0 1 0 0 14 7 7 0 0 0 0-14ZM16.5 16.5 21 21',
  fechar: 'M6 6l12 12M18 6 6 18',
  check: 'M4 12.5 9.5 18 20 6.5',
  alerta: 'M12 4 3 19h18L12 4ZM12 10v4M12 17h.01',
  mais: 'M12 5v14M5 12h14',
  menos: 'M5 12h14',
  anexo: 'M20 11.5 12 19.5a5 5 0 0 1-7-7l8-8a3.5 3.5 0 0 1 5 5l-8 8a2 2 0 0 1-3-3l7.5-7.5',
  bloqueio: 'M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18ZM5.6 5.6l12.8 12.8',
  camera: 'M7 4h10a3 3 0 0 1 3 3v10a3 3 0 0 1-3 3H7a3 3 0 0 1-3-3V7a3 3 0 0 1 3-3ZM12 8.5a3.5 3.5 0 1 0 0 7 3.5 3.5 0 0 0 0-7ZM16.6 7.4h.01',
  globo: 'M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18ZM3.4 9h17.2M3.4 15h17.2M12 3c2.4 2.4 3.6 5.4 3.6 9s-1.2 6.6-3.6 9c-2.4-2.4-3.6-5.4-3.6-9S9.6 5.4 12 3Z',
  ajuda: 'M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18ZM9.6 9.4a2.5 2.5 0 0 1 4.9.7c0 1.7-2.5 2.1-2.5 3.9M12 17h.01',
  dica: 'M9.5 18h5M10.5 21h3M12 3a6 6 0 0 0-3.6 10.8c.7.5 1 1.3 1.1 2h5c.1-.7.4-1.5 1.1-2A6 6 0 0 0 12 3Z',
  // Rodada 7 · assistente (seção D): termômetro de sentimento da conversa.
  // Tubo (duas linhas retas + tampa arredondada) e bulbo (mesma fórmula de
  // círculo de `relogio`, dois arcos), sem depender de curva nova.
  termometro: 'M10.5 15V5.5a1.5 1.5 0 0 1 3 0V15M12 14a3.5 3.5 0 1 0 0 7a3.5 3.5 0 0 0 0-7Z',

  // Rodada 3 · esteira (seção 5), sininho (seção 8) e comanda (seção 7) da
  // direção visual. Paths copiados do Lucide pelo nome que a direção deu, com
  // os subtraçados de cada `<path>`/`<circle>`/`<line>` original concatenados
  // num `d` só, no mesmo espírito dos ícones acima.
  'dollar-sign': 'M12 2v20M17 5H9.5a3.5 3.5 0 0 0 0 7h5a3.5 3.5 0 0 1 0 7H6',
  'circle-check': 'M12 2a10 10 0 1 0 0 20a10 10 0 0 0 0-20ZM16 9l-5.5 5.5L8 12',
  // No lugar de `panela` (que fica como está: ainda é usada fora do escopo
  // deste passe, em infra/ajuda.js e PainelAgente.jsx).
  'cooking-pot': 'M2 12h20M20 12v8a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2v-8M4 8l16-4'
    + 'M8.86 6.78l-.45-1.81a2 2 0 0 1 1.45-2.43l1.94-.48a2 2 0 0 1 2.43 1.46l.45 1.8',
  package: 'M11 21.73a2 2 0 0 0 2 0l7-4A2 2 0 0 0 21 16V8a2 2 0 0 0-1-1.73l-7-4a2 2 0 0 0-2 0l-7 4A2 2 0 0 0 3 8v8a2 2 0 0 0 1 1.73z'
    + 'M12 22V12M3.29 7L12 12L20.71 7M7.5 4.27l9 5.15',
  house: 'M15 21v-8a1 1 0 0 0-1-1h-4a1 1 0 0 0-1 1v8'
    + 'M3 10a2 2 0 0 1 .709-1.528l7-6a2 2 0 0 1 2.582 0l7 6A2 2 0 0 1 21 10v9a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2z',
  'circle-x': 'M12 2a10 10 0 1 0 0 20a10 10 0 0 0 0-20ZM15 9l-6 6M9 9l6 6',
  'undo-2': 'M9 14 4 9l5-5M4 9h10.5a5.5 5.5 0 0 1 5.5 5.5a5.5 5.5 0 0 1-5.5 5.5H11',
  'arrow-left': 'M12 19l-7-7 7-7M19 12H5',
  ellipsis: 'M12 11a1 1 0 1 0 0 2a1 1 0 0 0 0-2ZM19 11a1 1 0 1 0 0 2a1 1 0 0 0 0-2ZM5 11a1 1 0 1 0 0 2a1 1 0 0 0 0-2Z',
  'refresh-cw': 'M3 12a9 9 0 0 1 9-9 9.75 9.75 0 0 1 6.74 2.74L21 8M21 3v5h-5'
    + 'M21 12a9 9 0 0 1-9 9 9.75 9.75 0 0 1-6.74-2.74L3 16M8 16H3v5',
  'log-out': 'M16 17l5-5-5-5M21 12H9M9 21H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h4',
  hourglass: 'M5 22h14M5 2h14M17 22v-4.172a2 2 0 0 0-.586-1.414L12 12l-4.414 4.414A2 2 0 0 0 7 17.828V22'
    + 'M7 2v4.172a2 2 0 0 0 .586 1.414L12 12l4.414-4.414A2 2 0 0 0 17 6.172V2',
  bell: 'M10.268 21a2 2 0 0 0 3.464 0M3.262 15.326A1 1 0 0 0 4 17h16a1 1 0 0 0 .74-1.673'
    + 'C19.41 13.956 18 12.499 18 8A6 6 0 0 0 6 8c0 4.499-1.411 5.956-2.738 7.326',
  'bell-plus': 'M10.268 21a2 2 0 0 0 3.464 0M15 8h6M18 5v6'
    + 'M20.002 14.464a9 9 0 0 0 .738.863A1 1 0 0 1 20 17H4a1 1 0 0 1-.74-1.673C4.59 13.956 6 12.499 6 8a6 6 0 0 1 8.75-5.332',
  'grip-vertical': 'M9 11a1 1 0 1 0 0 2a1 1 0 0 0 0-2ZM9 4a1 1 0 1 0 0 2a1 1 0 0 0 0-2ZM9 18a1 1 0 1 0 0 2a1 1 0 0 0 0-2Z'
    + 'M15 11a1 1 0 1 0 0 2a1 1 0 0 0 0-2ZM15 4a1 1 0 1 0 0 2a1 1 0 0 0 0-2ZM15 18a1 1 0 1 0 0 2a1 1 0 0 0 0-2Z',
  printer: 'M6 18H4a2 2 0 0 1-2-2v-5a2 2 0 0 1 2-2h16a2 2 0 0 1 2 2v5a2 2 0 0 1-2 2h-2'
    + 'M6 9V3a1 1 0 0 1 1-1h10a1 1 0 0 1 1 1v6M6 14h12v8H6Z',
  lapis: 'M21.174 6.812a1 1 0 0 0-3.986-3.987L3.842 16.174a2 2 0 0 0-.5.83l-1.321 4.352a.5.5 0 0 0 .623.622l4.353-1.32a2 2 0 0 0 .83-.497zM15 5l4 4',

  // Rodada 5 · passo zero (seção 8): Balcão, Ficha, Cobrança, Encerramento e
  // Entregas. `x` e `plus` repetem o traço de `fechar`/`mais`: a direção
  // desta rodada nomeia os dois pelo nome do Lucide, então ganham chave
  // própria em vez de forçar quem chama a saber do apelido antigo.
  x: 'M6 6l12 12M18 6 6 18',
  plus: 'M12 5v14M5 12h14',
  search: 'M11 4a7 7 0 1 0 0 14 7 7 0 0 0 0-14ZM16.5 16.5 21 21',
  'arrow-up-down': 'M21 16l-4 4-4-4M17 20V4M3 8l4-4 4 4M7 4v16',
  hand: 'M8 13V4.5a1.5 1.5 0 0 1 3 0V12M11 12V3a1.5 1.5 0 0 1 3 0v9'
    + 'M14 12v-1.5a1.5 1.5 0 0 1 3 0V13M17 13a1.5 1.5 0 0 1 3 0v4a6 6 0 0 1-6 6h-2c-1.9 0-3-.6-4.2-2'
    + 'L4.8 16.3c-.6-.8-.5-1.7.2-2.3.7-.6 1.7-.5 2.3.2L8 15.5',
  lock: 'M6 11V7a6 6 0 0 1 12 0v4M5 11h14a1 1 0 0 1 1 1v9a1 1 0 0 1-1 1H5a1 1 0 0 1-1-1v-9a1 1 0 0 1 1-1ZM12 15v3',
  'user-plus': 'M9 11a4 4 0 1 0 0-8 4 4 0 0 0 0 8ZM2 21v-1a6 6 0 0 1 6-6h2a6 6 0 0 1 6 6v1M19 8v6M22 11h-6',
  'message-square-warning': 'M4 5h16a1 1 0 0 1 1 1v9a1 1 0 0 1-1 1H9l-5 4V6a1 1 0 0 1 1-1ZM12 9v3M12 15h.01',
  'thumbs-up': 'M7 22H4a1 1 0 0 1-1-1v-9a1 1 0 0 1 1-1h3M7 11l3.5-7a2 2 0 0 1 2 2.2L11.5 10H19'
    + 'a2 2 0 0 1 2 2.3l-1.4 7A2 2 0 0 1 17.6 21H7Z',
  'thumbs-down': 'M17 2h3a1 1 0 0 1 1 1v9a1 1 0 0 1-1 1h-3M17 13l-3.5 7a2 2 0 0 1-2-2.2L12.5 14H5'
    + 'a2 2 0 0 1-2-2.3l1.4-7A2 2 0 0 1 6.4 3H17Z',
  smile: 'M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18ZM8 14s1.5 2 4 2 4-2 4-2M9 9h.01M15 9h.01',
  meh: 'M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18ZM8 15h8M9 9h.01M15 9h.01',
  frown: 'M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18ZM16 16s-1.5-2-4-2-4 2-4 2M9 9h.01M15 9h.01',
  'external-link': 'M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V8a2 2 0 0 1 2-2h6M15 3h6v6M10 14 21 3',
  download: 'M12 15V3M7 10l5 5 5-5M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4',
  'map-pin': 'M12 21s7-6.5 7-12a7 7 0 1 0-14 0c0 5.5 7 12 7 12ZM12 12a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5Z',
  'credit-card': 'M3 6h18a1 1 0 0 1 1 1v10a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1V7a1 1 0 0 1 1-1ZM2 10h20M6 15h4',
  'flask-conical': 'M9 3h6M10 3v6.5L4.5 19a1.5 1.5 0 0 0 1.3 2.3h12.4a1.5 1.5 0 0 0 1.3-2.3L14 9.5V3M7 16h10',
  'chevron-up': 'M6 15l6-6 6 6',
  // M1.1 (#1481): descer um item na lista de gestão do cardápio.
  'chevron-down': 'M6 9l6 6 6-6',
  'chevron-right': 'M9 18l6-6-6-6',
  // F2 · Ficha do cliente (rodada 5, seção 2): "Copiar telefone" no menu ⋯.
  // Rodada 6c: ícones do trilho, da ficha e do composer.
  phone: 'M22 16.9v3a2 2 0 0 1-2.2 2 19.8 19.8 0 0 1-8.6-3.1 19.5 19.5 0 0 1-6-6A19.8 19.8 0 0 1 2.1 4.2 2 2 0 0 1 4.1 2h3a2 2 0 0 1 2 1.7c.1 1 .4 1.9.7 2.8a2 2 0 0 1-.5 2.1L8.1 9.9a16 16 0 0 0 6 6l1.3-1.3a2 2 0 0 1 2.1-.4c.9.3 1.9.6 2.8.7a2 2 0 0 1 1.7 2Z',
  sun: 'M12 16a4 4 0 1 0 0-8 4 4 0 0 0 0 8ZM12 2v2M12 20v2M4.9 4.9l1.4 1.4M17.7 17.7l1.4 1.4M2 12h2M20 12h2M6.3 17.7l-1.4 1.4M19.1 4.9l-1.4 1.4',
  moon: 'M12 3a6 6 0 0 0 9 9 9 9 0 1 1-9-9Z',
  inbox: 'M22 12h-6l-2 3h-4l-2-3H2M5.5 5.1 2 12v6a2 2 0 0 0 2 2h16a2 2 0 0 0 2-2v-6l-3.5-6.9A2 2 0 0 0 16.8 4H7.2a2 2 0 0 0-1.7 1.1Z',
  'arrow-up': 'M12 19V5M5 12l7-7 7 7',
  respostas: 'M21 15a2 2 0 0 1-2 2H7l-4 4V5a2 2 0 0 1 2-2h14a2 2 0 0 1 2 2ZM8 9h8M8 13h5',
  // Rodada 6d: pedidos da frente de anexos e do assistente (rodada 7) e da banca
  // estética (glifo de canal reconhecível).
  mic: 'M12 2a3 3 0 0 0-3 3v7a3 3 0 0 0 6 0V5a3 3 0 0 0-3-3ZM19 10v2a7 7 0 0 1-14 0v-2M12 19v3',
  link: 'M10 13a5 5 0 0 0 7.5.5l3-3a5 5 0 0 0-7-7l-1.7 1.7M14 11a5 5 0 0 0-7.5-.5l-3 3a5 5 0 0 0 7 7l1.7-1.7',
  sparkles: 'M12 3l1.9 5.1L19 10l-5.1 1.9L12 17l-1.9-5.1L5 10l5.1-1.9L12 3ZM19 3v4M17 5h4M5 17v4M3 19h4',
  play: 'M7 4.5v15a1 1 0 0 0 1.5.9l12-7.5a1 1 0 0 0 0-1.8l-12-7.5A1 1 0 0 0 7 4.5Z',
  pause: 'M7 4h3v16H7zM14 4h3v16h-3z',
  whatsapp: 'M3 21l1.7-4.9A9 9 0 1 1 8 19.6L3 21ZM9 8.5c0 3.3 3.2 6.5 6.5 6.5l1.5-1.5-2-1.2-1 1a4.5 4.5 0 0 1-2.8-2.8l1-1-1.2-2L9 8.5Z',
  instagram: 'M7 3h10a4 4 0 0 1 4 4v10a4 4 0 0 1-4 4H7a4 4 0 0 1-4-4V7a4 4 0 0 1 4-4ZM12 16a4 4 0 1 0 0-8 4 4 0 0 0 0 8ZM17.5 6.5h.01',
  copy: 'M9 9h11a1 1 0 0 1 1 1v11a1 1 0 0 1-1 1H9a1 1 0 0 1-1-1V10a1 1 0 0 1 1-1ZM5 15H4a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1h10a1 1 0 0 1 1 1v1',

  // Rodada 7 · Cardápio por link (US-021): carrinho do site do cliente e
  // resumo na ficha/composer.
  'shopping-cart': 'M3 4h2.2l2.68 13.4a2 2 0 0 0 2 1.6h8.24a2 2 0 0 0 1.97-1.66L21.4 8H6'
    + 'M9 21a1.3 1.3 0 1 0 0-2.6 1.3 1.3 0 0 0 0 2.6ZM18 21a1.3 1.3 0 1 0 0-2.6 1.3 1.3 0 0 0 0 2.6Z',
  // Frente Anexos (rodada 7, pedido do dono 24/09/2026 04h12): "tirar" peça
  // da galeria. Sem par pronto no lote do visual, entra sozinho.
  lixeira: 'M3 6h18M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2M10 11v6M14 11v6',
  // Rodada 7 · biblioteca de respostas (features/respostas): "Arquivar" uma
  // resposta pronta, nunca some, só sai da lista padrão.
  archive: 'M3 3h18a1 1 0 0 1 1 1v3a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1V4a1 1 0 0 1 1-1ZM4 8v11a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8M10 12h4',

  // Rodada 13 · casca da Gestão (features/gestao): quatro blocos de tamanho
  // desigual, o "layout-dashboard" do Lucide, byte a byte como os outros
  // ícones emprestados dele (package, house). Trilho e topo abrem a mesma
  // gaveta com este ícone.
  painel: 'M3 3h7v9H3zM14 3h7v5h-7zM14 12h7v9h-7zM3 16h7v5H3z',

  // Rodada 13 · Fidelidade e cupons (issue #45, registro 107): selo de pontos
  // na ficha do cliente e recompensa/sorteio na Gestão. "star"/"gift" do
  // Lucide, byte a byte como os outros ícones emprestados dele.
  estrela: 'M11.48 3.5a.56.56 0 0 1 1.04 0l2.13 5.11a.56.56 0 0 0 .47.35l5.52.44c.5.04.7.66.32.99l-4.2 3.6a.56.56 0 0 0-.19.56l1.29 5.39a.56.56 0 0 1-.84.6l-4.73-2.88a.56.56 0 0 0-.58 0l-4.73 2.88a.56.56 0 0 1-.84-.6l1.29-5.39a.56.56 0 0 0-.19-.56l-4.2-3.6a.56.56 0 0 1 .32-.99l5.52-.44a.56.56 0 0 0 .47-.35Z',
  presente: 'M20 12v10H4V12M2 7h20v5H2zM12 22V7M12 7H7.5a2.5 2.5 0 0 1 0-5C11 2 12 7 12 7ZM12 7h4.5a2.5 2.5 0 0 0 0-5C13 2 12 7 12 7Z',
}

export function Icone({ nome, tamanho = '1.15em', rotulo, className }) {
  const traco = TRACOS[nome]
  if (!traco) return null
  // `tamanho` aceita número em px (secao 10 da direção visual), além do `em`
  // de sempre para crescer com a fonte do sistema.
  const medida = typeof tamanho === 'number' ? `${tamanho}px` : tamanho
  return (
    <svg
      className={className ? `${css.icone} ${className}` : css.icone}
      width={medida}
      height={medida}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.75"
      strokeLinecap="round"
      strokeLinejoin="round"
      role={rotulo ? 'img' : 'presentation'}
      aria-label={rotulo}
      aria-hidden={rotulo ? undefined : 'true'}
    >
      <path d={traco} />
    </svg>
  )
}
