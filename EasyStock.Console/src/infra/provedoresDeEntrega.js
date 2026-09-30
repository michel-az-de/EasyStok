// Fachada de despacho por PROVEDOR, mesmo espírito de
// `infra/provedoresDeCobranca.js`: ninguém fora daqui (nem o domínio, nem
// componente nenhum) conhece Lalamove ou 99 Entregas por nome, só chama
// `cotar`/`chamar`/`cancelar`/`consultarStatus` e recebe de volta a MESMA
// forma sempre. Issue #46 (registro 108), áudios 06 e da homologação: "saber
// quando entregou pelo número do pedido, como o iFood".
//
// ---------------------------------------------------------------------------
// AQUI ENTRAM AS APIS REAIS DA LALAMOVE E DA 99 ENTREGAS.
//
// Contrato do adapter (para o backend do EasyStok implementar, um por
// `ProviderKey` dentro de `CategoriaIntegracao.Logistics`, com a
// `CredencialIntegracao` do tenant):
//
//   cotar({ provedor, pedido, bairro })   -> Promise<{ preco, prazoMin }>
//     Cotação antes de chamar (Lalamove: POST /v3/quotations; 99: endpoint de
//     estimativa). O protótipo simula com um preço e prazo determinísticos a
//     partir do pedido e do bairro, para a MESMA demo repetir a MESMA cotação.
//
//   chamar({ provedor, pedido, cotacaoId }) -> Promise<{ codigo }>
//     Efetiva a corrida (Lalamove: POST /v3/orders a partir da cotação; 99:
//     endpoint de criação de entrega). `codigo` é o identificador que o
//     entregador e o rastreio do provedor usam, e que o protótipo liga ao
//     NÚMERO DO PEDIDO (`numeroParaEntregador`, `dominio/despacho.js`) do
//     mesmo jeito que o iFood já faz.
//
//   cancelar({ provedor, codigo }) -> Promise<{ status: 'cancelado' }>
//     Desiste da corrida em aberto (Lalamove: PATCH /v3/orders/:id/cancel).
//
//   consultarStatus({ provedor, codigo }) -> Promise<{ status, entregador }>
//     Sondagem manual, só de apoio: em produção quem manda o status é o
//     WEBHOOK abaixo, não polling.
//
//   webhook: shape do evento que o provedor empurra (Lalamove:
//     `ORDER_STATUS_CHANGED`/`DRIVER_ASSIGNED`; 99: callback de status),
//     ponto único que o backend recebe e traduz para o mesmo vocabulário:
//       { codigo, status: 'procurando'|'a-caminho-coleta'|'coletado'|'entregue'|'falha',
//         entregador: { nome, veiculo, placa } | null, em }
//     RN-32/D8 (a regra que esta rodada reforça): o status muda a esteira
//     porque o PROVEDOR avisou, nunca porque o app adivinhou pelo relógio.
//
// O protótipo não faz nenhuma chamada de rede: as três funções abaixo são
// puras/determinísticas (mesmo pedido + provedor => mesma cotação e mesmo
// código, para a demo ser reproduzível), e `consultarStatus` é quem simula o
// avanço da corrida com um tique de tela chamando de novo, no mesmo padrão de
// `dominio/viagem.js` § "Chamar entregador" (rodada 5) e do comentário em
// `features/entregas/PainelViagem.jsx`.
// ---------------------------------------------------------------------------
import { proximoStatusCorrida } from '../dominio/corrida'

const PREFIXO_CODIGO = { lalamove: 'LM', '99entrega': '99E' }

// Hash pequeno e estável (sem crypto, sem Math.random): mesmo texto sempre
// vira o mesmo número, para a cotação e o código não mudarem a cada render.
function hashEstavel(texto) {
  let h = 0
  for (const char of String(texto)) h = (h * 31 + char.charCodeAt(0)) >>> 0
  return h
}

const PRECO_BASE = 8.9
const PRECO_POR_BAIRRO = 0.35

// Cotação simulada: preço cresce um pouco com o hash do bairro (bairro mais
// "longe" no hash paga mais), prazo entre 18 e 38 min. Nada disto é distância
// real (o protótipo não tem mapa/rota, `dominio/alcance.js` só agrupa por
// bairro); é o suficiente para a demo mostrar preço e prazo coerentes e
// estáveis pedido a pedido.
export async function cotar({ provedor, pedido, bairro }) {
  const semente = hashEstavel(`${provedor}|${pedido?.numero}|${bairro ?? ''}`)
  const preco = Math.round((PRECO_BASE + (semente % 20) * PRECO_POR_BAIRRO) * 100) / 100
  const prazoMin = 18 + (semente % 21)
  return { preco, prazoMin }
}

let sequenciaCorrida = 0

export async function chamar({ provedor, pedido }) {
  sequenciaCorrida += 1
  const prefixo = PREFIXO_CODIGO[provedor] ?? 'COR'
  const semente = hashEstavel(`${provedor}|${pedido?.numero}|${sequenciaCorrida}`)
  const codigo = `${prefixo}${String(semente % 100000).padStart(5, '0')}`
  return { codigo }
}

export async function cancelar() {
  return { status: 'cancelado' }
}

// Nomes fictícios do "achado" simulado, mesmo espírito do `ACHADO_SIMULADO`
// de `PainelViagem.jsx` (R12): quando a corrida entrega um entregador, ele
// vem no formato que `dominio/despacho.js` já entende (nome, veículo, placa).
const ENTREGADORES_SIMULADOS = {
  lalamove: { nome: 'Rafael Souza', veiculo: 'moto', placa: 'LLM4A12' },
  '99entrega': { nome: 'Douglas Lima', veiculo: 'moto', placa: 'NVE9B34' },
}

// Avança o status simulado, com uma chance pequena de falha em quem ainda não
// coletou (achado do estudo da homologação: "às vezes o app não acha
// ninguém" ou "o entregador desiste antes de chegar"). `aleatorio` é
// injetável pela prova (`ferramentas/prova-r13-integracoes.mjs`) para o teste
// não depender de sorte.
const CHANCE_DE_FALHA = 0.12

// `entregador` só vem preenchido no tique que sai de "procurando": é quando o
// provedor "achou" alguém (equivalente ao webhook `DRIVER_ASSIGNED`). Nos
// tiques seguintes vem `null`, e quem chama mantém o registro anterior (mesmo
// padrão do `atualizarChamado` da R12 em `PainelViagem.jsx`).
export async function consultarStatus({ provedor, status, aleatorio = Math.random }) {
  if (status !== 'coletado' && status !== 'entregue' && aleatorio() < CHANCE_DE_FALHA) {
    return { status: 'falha', entregador: null }
  }
  const proximo = proximoStatusCorrida(status)
  if (!proximo) return { status, entregador: null }
  const entregador = status === 'procurando' ? (ENTREGADORES_SIMULADOS[provedor] ?? null) : null
  return { status: proximo, entregador }
}
