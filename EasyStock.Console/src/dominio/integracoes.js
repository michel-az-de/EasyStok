// Domínio da Frente Entregas e integrações (rodada 13, issue #46, registro
// 108). Puro, sem React: o cadastro de provedores de logística que a aba de
// Gestão liga/desliga e o despacho usa para escolher quem chama.
//
// Nomeação alinhada ao EasyStok (mapa da R13, `auditoria` do backend): lá o
// provedor é um `ProviderKey` dentro de `CategoriaIntegracao.Logistics`, com
// `CredencialIntegracao` por tenant. Nenhum adapter existe ainda no ERP; este
// arquivo é o espelho da MESMA modelagem no protótipo, para o nome não
// precisar mudar quando o backend chegar. "Entregador próprio" não é uma
// integração de verdade (não tem credencial, é a casa mesma buscando quem já
// conhece, fluxo herdado da R12/issue #17): ele entra no mesmo cadastro para
// ligar/desligar e virar padrão, mas nunca pede credencial.
export const PROVEDORES_LOGISTICA = [
  { chave: 'proprio', rotulo: 'Entregador próprio', exigeCredencial: false, icone: 'moto' },
  { chave: 'lalamove', rotulo: 'Lalamove', exigeCredencial: true, icone: 'moto' },
  { chave: '99entrega', rotulo: '99 Entregas', exigeCredencial: true, icone: 'moto' },
]

export const provedorPorChave = (chave) => PROVEDORES_LOGISTICA.find((p) => p.chave === chave) ?? null

// Estado de partida: só "Entregador próprio" ligado (é o fluxo que já existia
// antes desta rodada, RN-32/R12), sem credencial nenhuma guardada. Ligar
// Lalamove ou 99 é decisão da Thati; o protótipo nasce sem presumir isso
// (proposta para a Thati: ela decide quando ligar de verdade).
export function configuracaoPadraoIntegracoes() {
  return {
    padrao: 'proprio',
    provedores: {
      proprio: { ativo: true, credencial: null },
      lalamove: { ativo: false, credencial: null },
      '99entrega': { ativo: false, credencial: null },
    },
  }
}

const configDoProvedor = (config, chave) => config?.provedores?.[chave] ?? { ativo: false, credencial: null }

export const temCredencial = (config, chave) => Boolean(configDoProvedor(config, chave).credencial)

// Motivo por escrito de por que este provedor ainda não liga, `null` quando
// pode ligar. Só quem exige credencial (Lalamove, 99) trava sem ela: a
// integração real não autentica sem chave de API, e o protótipo simula essa
// borda em vez de fingir que qualquer toque liga.
export function motivoParaAtivar(config, chave) {
  const provedor = provedorPorChave(chave)
  if (!provedor) return 'Provedor desconhecido.'
  if (!provedor.exigeCredencial) return null
  return temCredencial(config, chave) ? null : 'Cadastre a credencial antes de ligar.'
}

export function alternarProvedor(config, chave) {
  const atual = configDoProvedor(config, chave)
  if (!atual.ativo && motivoParaAtivar(config, chave)) return config
  const ativo = !atual.ativo
  const provedores = { ...config.provedores, [chave]: { ...atual, ativo } }
  // Desligar o provedor que era o padrão devolve o padrão para "próprio"
  // (sempre ligado por padrão): nunca fica um padrão apontando para um
  // provedor desligado, que travaria o despacho em silêncio.
  const padrao = !ativo && config.padrao === chave ? 'proprio' : config.padrao
  return { ...config, provedores, padrao }
}

// A credencial só entra em memória do estado da sessão: nunca é lida de volta
// pela tela (a aba nunca recebe o valor gravado, só o booleano `temCredencial`
// acima) e nenhuma função deste arquivo imprime, loga ou exporta o texto.
export function salvarCredencial(config, chave, credencial) {
  const provedor = provedorPorChave(chave)
  if (!provedor?.exigeCredencial) return config
  const limpa = String(credencial ?? '').trim()
  const atual = configDoProvedor(config, chave)
  return {
    ...config,
    provedores: { ...config.provedores, [chave]: { ...atual, credencial: limpa || null } },
  }
}

export function podeSerPadrao(config, chave) {
  return configDoProvedor(config, chave).ativo && !motivoParaAtivar(config, chave)
}

export function definirPadrao(config, chave) {
  if (!podeSerPadrao(config, chave)) return config
  return { ...config, padrao: chave }
}

// Provedores que o despacho pode oferecer agora (ligados e sem pendência de
// credencial), na ordem do cadastro, com o padrão primeiro. Usado por
// `EscolhaDeProvedorEntrega` para montar os chips.
export function provedoresParaDespacho(config) {
  const ligados = PROVEDORES_LOGISTICA.filter((p) => configDoProvedor(config, p.chave).ativo)
  return ligados
    .slice()
    .sort((a, b) => (a.chave === config.padrao ? -1 : b.chave === config.padrao ? 1 : 0))
    .map((p) => ({ chave: p.chave, rotulo: p.rotulo, exigeCredencial: p.exigeCredencial }))
}

export const AVISO_SIMULADO = 'Simulado: nenhuma chamada sai para a internet. Lalamove e 99 Entregas ainda não '
  + 'estão ligados de verdade ao sistema.'
