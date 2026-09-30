import {
  ADICIONAIS, ADICIONAIS_POR_SKU, CANAIS, CARDAPIO, ENDERECO_DA_CASA, ESTADOS_CONVERSA,
  INSTANTE_INICIAL, JANELAS_ENTREGA, LINHAS_PRODUTO, MEIOS_DE_PAGAMENTO, MINUTOS_CHEGADA_ENTREGADOR,
  MINUTOS_CONFERIR, MINUTOS_TRECHO, MODELOS, MOTIVOS_BLOQUEIO, PECA_HORARIO_PADRAO,
  PREFIXOS_CEP_ATENDIDOS, RESPIRO_MINUTOS_PADRAO, RESPOSTAS_PRONTAS, RESPOSTAS_RAPIDAS, RESTRICOES,
  TEMPLATES_INTERNOS,
} from './catalogo'
import { cartaDoItem } from './imagensCardapio'
import { ENTREGADORES_CADASTRADOS } from './entregadoresSemente'
import { tituloDaCarta } from '../dominio/arteCardapio'
import { configuracaoPadraoIntegracoes } from '../dominio/integracoes'

// Carta genérica para peça sem foto de prato (hoje só "Horário de
// funcionamento"): mesmo espírito honesto de `imagensCardapio.js` ("parece
// arte de cardápio, não finge ser fotografia"), um cartão âmbar com relógio.
const cartaDeAviso = (titulo) => 'data:image/svg+xml;utf8,' + encodeURIComponent(
  '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 320 200">'
  + '<rect width="320" height="200" fill="#7A5600"/>'
  + '<circle cx="82" cy="100" r="46" fill="none" stroke="#F0D77A" stroke-width="6"/>'
  + '<path d="M82 74v28l18 14" stroke="#F0D77A" stroke-width="6" fill="none" stroke-linecap="round"/>'
  + tituloDaCarta(titulo, 94).svg
  + `<text x="156" y="${tituloDaCarta(titulo, 94).fim + 26}" font-family="Helvetica,Arial" font-size="11" fill="#F6EFE299">CASA DA BABA</text>`
  + '</svg>',
)

// Galeria inicial (rodada 7, frente Anexos): uma peça por prato do cardápio
// do dia, com a mesma arte que o antigo botão "Prato" já mandava, mais a
// peça de "Horário de funcionamento" pedida pelo dono. A partir daqui cada
// peça é registro independente (nome/descrição/foto próprios): editar o
// cardápio não edita a galeria, e vice-versa.
const galeriaPadrao = () => [
  { ...PECA_HORARIO_PADRAO, foto: cartaDeAviso(PECA_HORARIO_PADRAO.nome) },
  ...CARDAPIO.map((item) => ({
    id: `peca-cardapio-${item.sku}`,
    nome: item.nome,
    descricao: `Porção de ${item.porcao}.`,
    foto: cartaDoItem(item),
  })),
]

// Porta de entrada do catálogo. Trocar por HTTP mexe só neste arquivo e no
// provider, nunca em componente.
export function carregarCatalogo() {
  return {
    canais: CANAIS,
    estadosConversa: ESTADOS_CONVERSA,
    // Adicional entra no mesmo cardápio: um lugar só tem preço e saldo.
    cardapio: [...CARDAPIO, ...ADICIONAIS],
    adicionais: ADICIONAIS_POR_SKU,
    linhas: LINHAS_PRODUTO,
    janelas: JANELAS_ENTREGA,
    // Rodada 13 (issue #42, RN-22): respiro mínimo de entrega, ajustável na
    // tela de Janelas sem nunca descer de `RESPIRO_MINIMO_RN22` (40 min).
    respiroMinutos: RESPIRO_MINUTOS_PADRAO,
    // Rodada 5, seção 4 (escolha de meio): mesma porta de entrada, mesmo
    // motivo das constantes de Entregas logo abaixo. `BlocoCobranca` lê os
    // chips daqui, nunca de `infra/catalogo.js` direto.
    meiosDePagamento: MEIOS_DE_PAGAMENTO,
    // Área de entrega (RN-09/RN-10/RN-11): dominio/areaEntrega.js é puro e
    // não importa infra, então o prefixo atendido chega por aqui.
    prefixosCepAtendidos: PREFIXOS_CEP_ATENDIDOS,
    // Rodada 5, seção 6 (Entregas de hoje): a F6 lê estas constantes pelo
    // contexto do catálogo, porque `features/*` não pode importar `infra/*`
    // direto (`ferramentas/verificar-camadas.mjs`). Esta é a única porta de
    // entrada do catálogo para a árvore de componentes. `instanteInicial`
    // vai junto porque a janela de Entregas (`TelaEntregas.jsx`) tem o
    // próprio relógio (`useRelogio`) e precisa partir do mesmo instante do
    // Balcão para os dois lados não desencontrarem a hora.
    enderecoDaCasa: ENDERECO_DA_CASA,
    minutosTrecho: MINUTOS_TRECHO,
    minutosChegadaEntregador: MINUTOS_CHEGADA_ENTREGADOR,
    minutosConferir: MINUTOS_CONFERIR,
    // Rodada 12 (issue #17): entregadores de um toque no despacho (ficha,
    // cozinha e Entregas), pela mesma porta do catálogo.
    entregadores: ENTREGADORES_CADASTRADOS,
    // Rodada 13 (issue #46, registro 108): provedores de logística (Lalamove,
    // 99 Entregas, Entregador próprio) que a aba de Gestão liga/desliga.
    // Nasce só com "próprio" ligado, o mesmo comportamento que já existia.
    integracoesLogistica: configuracaoPadraoIntegracoes(),
    instanteInicial: INSTANTE_INICIAL,
    respostasRapidas: RESPOSTAS_RAPIDAS,
    modelos: MODELOS,
    templates: TEMPLATES_INTERNOS,
    // Rodada 7 (pedido do dono 24/09/2026): biblioteca editável, na tela de
    // Respostas do composer (features/respostas). Semente vem pronta de
    // infra/catalogo.js; incluir/editar/arquivar mexe só nesta chave.
    respostasProntas: RESPOSTAS_PRONTAS,
    motivosBloqueio: MOTIVOS_BLOQUEIO,
    // F2 · Ficha do cliente (seção 2, tags): feature não importa infra direto
    // (verificar-camadas.mjs), então a lista de restrições chega pelo catálogo.
    restricoes: RESTRICOES,
    // Rodada 7 · frente Anexos: fotos prontas para enviar na conversa (cadastro
    // da dona, seed com um cartão por prato do dia + "Horário de funcionamento").
    galeria: galeriaPadrao(),
  }
}

