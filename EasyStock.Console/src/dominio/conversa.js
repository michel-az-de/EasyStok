import { precisaDeVoce } from './automatico'
import { inicioDoPedidoAtivo } from './entrega'

export const ehLead = (conversa) => conversa.conta === 'lead'

// Rótulo em vez de conteúdo cru (seção 1, rodada 5): mídia vira ícone e
// palavra no cartão do Balcão, texto de mensagem não cabe numa linha só.
// 'arquivo', 'audio' e 'peca' são da frente Anexos (rodada 7).
const ROTULO_DO_FORMATO = {
  figurinha: 'Figurinha', imagem: 'Foto', arquivo: 'Arquivo', audio: 'Áudio', peca: 'Foto',
}

// Prefixo em 600 diz quem escreveu por último sem abrir a conversa:
// "Automático:" quando foi a regra que respondeu, "Você:" quando foi a mão da
// dona, nada quando foi o cliente. Devolve `{ prefixo, corpo }` em vez de uma
// string só porque só o prefixo é 600 (seção 1); quem monta o texto plano
// (título, aria-label) faz `prefixo + corpo`.
export function previaDaConversa(conversa) {
  const ultima = conversa.mensagens.at(-1)
  if (!ultima) return { prefixo: '', corpo: 'Sem mensagens' }
  const prefixo = ultima.dir === 'sistema'
    ? 'Sistema: '
    : ultima.dir === 'out'
      ? (ultima.automatica ? 'Automático: ' : 'Você: ')
      : ''
  return { prefixo, corpo: ROTULO_DO_FORMATO[ultima.formato] ?? ultima.texto }
}

// Aberto é o estado saudável de um painel de atendimento, não um alerta.
// Âmbar fica reservado ao que espera ação da dona.
export function tomDoEstado(estado) {
  if (estado === 'Em atendimento') return 'ok'
  return 'neutro'
}

export const conversaEncerrada = (conversa) => conversa?.estado === 'Encerrado'

// Contagens do cabeçalho. Ficam aqui para o layout não conhecer estado nenhum.
export const contarAbertas = (conversas) =>
  conversas.filter((c) => c.estado !== 'Encerrado').length

export const contarNaEsteira = (conversas) =>
  conversas.filter((c) => c.pedido && c.pedido.estado !== 'entregue').length

// --- Balcão (seção 1, rodada 5) ---------------------------------------------
// Duas abas (Precisa de você / Todas), chips de canal que ligam mais de um ao
// mesmo tempo (nenhum ligado é todos) e uma busca. Situação e bloqueado saíram
// de filtro: bloqueado e encerrado viram grupo recolhido no fim de "Todas", e
// não entram na lista de cima nem lá nem em "Precisa de você".

// US-016: achar cliente pela tag é a mesma busca do Balcão, não um filtro
// novo (o Balcão acabou de ser enxugado, seção 1). Tag entra na mesma conta
// de nome/telefone/mensagem.
//
// Rodada 10 (achado P1.3, US-052 "filtrar quem já comprou lasanha"): a busca
// também casa contra `conversa.historico` (anexado por `infra/massaConversas.js`
// a partir de `infra/historicoPedidos.js`), não só contra a conversa de hoje.
// Sem isso, quem só falou de lasanha meses atrás e hoje só troca mensagem
// sobre outra coisa nunca aparecia buscando "lasanha", mesmo tendo comprado.
export const combinaBusca = (conversa, busca) => {
  const termo = (busca ?? '').trim().toLowerCase()
  if (!termo) return true
  return conversa.nome.toLowerCase().includes(termo)
    || (conversa.cliente?.telefone ?? '').includes(termo)
    || (conversa.cliente?.tags ?? []).some((tag) => tag.toLowerCase().includes(termo))
    || conversa.mensagens.some((m) => (m.texto ?? '').toLowerCase().includes(termo))
    || (conversa.historico ?? []).some((pedido) => pedido.itens.some((item) => item.toLowerCase().includes(termo)))
}

// Qual tag respondeu pela busca (US-016): usado só pra mostrar no cartão o
// porquê achou, quando o nome sozinho não explicaria ("lasanha" achando
// "Marina" por causa da tag "Gosta de lasanha"). `null` quando não foi a tag
// que achou.
export function tagCorrespondida(conversa, busca) {
  const termo = (busca ?? '').trim().toLowerCase()
  if (!termo) return null
  return (conversa.cliente?.tags ?? []).find((tag) => tag.toLowerCase().includes(termo)) ?? null
}

// Chip de canal liga e desliga (zero ou mais). Nenhum ligado é "todos", por
// isso não existe chip "Todos" nem valor `''` aqui: a lista vazia já significa
// sem filtro de canal.
export const combinaCanal = (conversa, canais) =>
  !canais || canais.length === 0 || canais.includes(conversa.canal)

const foraDoTopo = (conversa) => estaBloqueada(conversa) || conversa.estado === 'Encerrado'

// Lista principal do Balcão: nunca traz bloqueado nem encerrado (grupo próprio
// no fim de "Todas"), respeita canal e busca sempre, e a aba "Precisa de você"
// some com quem o automático ainda vai responder sozinho.
export function conversasDoBalcao(conversas, filtros, agora, automaticoPausado = {}, aberta = true, janelas = []) {
  const base = conversas.filter((c) =>
    !foraDoTopo(c) && combinaCanal(c, filtros.canais) && combinaBusca(c, filtros.busca))
  if (filtros.aba !== 'precisa') return base
  return base.filter((c) => precisaDeVoce(c, agora, automaticoPausado[c.id] ?? false, aberta, janelas))
}

// Os dois grupos recolhidos do fim de "Todas" (seção 1): mesma busca e mesmo
// canal da lista de cima, sem a aba, porque nenhum dos dois existe em
// "Precisa de você".
export const conversasEncerradasDoBalcao = (conversas, filtros) =>
  conversas.filter((c) =>
    !estaBloqueada(c) && c.estado === 'Encerrado'
    && combinaCanal(c, filtros.canais) && combinaBusca(c, filtros.busca))

export const conversasBloqueadasDoBalcao = (conversas, filtros) =>
  conversas.filter((c) =>
    estaBloqueada(c) && combinaCanal(c, filtros.canais) && combinaBusca(c, filtros.busca))

// Contador por canal do topo do Balcão (issue #2). Conta o que está aberto na
// lista de cima (nem encerrada nem bloqueada) e, dentro disso, quantas
// precisam de você. Ignora busca e canal ligado de propósito: ligar um canal
// ou digitar na busca não pode mudar o número dos outros canais.
export function contarPorCanal(conversas, canais, agora, automaticoPausado = {}, aberta = true, janelas = []) {
  return (canais ?? []).map((canal) => {
    const abertas = conversas.filter((c) => c.canal === canal.nome && !foraDoTopo(c))
    return {
      nome: canal.nome,
      abertas: abertas.length,
      precisam: abertas
        .filter((c) => precisaDeVoce(c, agora, automaticoPausado[c.id] ?? false, aberta, janelas)).length,
    }
  })
}

// Três ordens (seção 1), um botão só. `janelas` só importa para "entrega": sem
// pedido ativo ou sem janela escolhida, a conversa vai para o fim dessa ordem.
export function ordenarBalcao(conversas, agora, {
  ordenacao = 'urgencia', automaticoPausado = {}, janelas = [], aberta = true,
} = {}) {
  const lista = [...conversas]
  if (ordenacao === 'recentes') {
    return lista.sort((a, b) => new Date(b.ultimaEm).getTime() - new Date(a.ultimaEm).getTime())
  }
  if (ordenacao === 'entrega') {
    return lista.sort((a, b) => {
      const inicioA = inicioDoPedidoAtivo(a, janelas, agora) ?? Infinity
      const inicioB = inicioDoPedidoAtivo(b, janelas, agora) ?? Infinity
      if (inicioA !== inicioB) return inicioA - inicioB
      return new Date(a.ultimaEm).getTime() - new Date(b.ultimaEm).getTime()
    })
  }
  // Padrão "Urgência": quem precisa dela sobe primeiro, e dentro de cada
  // grupo quem espera há mais tempo sobe.
  return lista.sort((a, b) => {
    const prioridadeA = precisaDeVoce(a, agora, automaticoPausado[a.id] ?? false, aberta, janelas) ? 1 : 0
    const prioridadeB = precisaDeVoce(b, agora, automaticoPausado[b.id] ?? false, aberta, janelas) ? 1 : 0
    if (prioridadeA !== prioridadeB) return prioridadeB - prioridadeA
    return new Date(a.ultimaEm).getTime() - new Date(b.ultimaEm).getTime()
  })
}

// Só é cliente da casa quem já recebeu alguma coisa. Confirmar endereço cria
// cadastro, não cria histórico, e a ficha não pode dizer o contrário.
export const jaComprou = (cliente) => cliente.pedidos > 0 && Boolean(cliente.desde)

// UMA contagem de pedidos na ficha inteira, e ela sai de `cliente.pedidos`, que
// é o dado da conversa. Antes o número aparecia aqui e no título do histórico,
// vindo de duas fontes que podiam discordar. "Freguesa" saiu junto: o cadastro
// não tem gênero e a casa atende homem, mulher e empresa.
export function situacaoDoCliente(cliente) {
  const quantos = cliente.pedidos === 1 ? '1 pedido' : `${cliente.pedidos} pedidos`
  if (jaComprou(cliente)) return `Cliente desde ${cliente.desde} · ${quantos}`
  if (cliente.endereco) return 'Cadastro criado, primeira compra ainda não saiu'
  return 'Ainda não comprou'
}

// --- Bloqueio de cadastro (RN-14) -------------------------------------------
// O bloqueio é do cadastro, nunca de uma conversa só: vale ao mesmo tempo em
// WhatsApp, Instagram e chat do site. Tudo aqui é função pura, o reducer é
// quem guarda o resultado.

export const TAG_BLOQUEADO = 'Bloqueado'

export const estaBloqueada = (conversa) => Boolean(conversa?.bloqueio)

export const bloqueioDe = (conversa) => conversa?.bloqueio ?? null

// `em` chega já formatado para leitura, do mesmo jeito que a nota da ficha.
export const novoBloqueio = ({ motivo, em, por }) => ({
  motivo: motivo.trim(), em, por, alcance: 'todos os canais',
})

// Rodada 5, seção 2: `cadastroId` no lugar do nome. Renomear na edição em
// linha não pode quebrar o bloqueio (RN-14) nem o vínculo de domicílio, e os
// dois viviam comparando `nome` até aqui.
export const mesmoCadastro = (conversa, cadastroId) => conversa.cadastroId === cadastroId

export const conversasDoCadastro = (conversas, cadastroId) =>
  conversas.filter((c) => mesmoCadastro(c, cadastroId))

export const bloquearCadastro = (conversas, cadastroId, bloqueio) =>
  conversas.map((c) => (mesmoCadastro(c, cadastroId) ? { ...c, bloqueio } : c))

export const desbloquearCadastro = (conversas, cadastroId) =>
  conversas.map((c) => (mesmoCadastro(c, cadastroId) ? { ...c, bloqueio: null } : c))

export const contarBloqueadas = (conversas) => conversas.filter(estaBloqueada).length

export function resumoDoBloqueio(bloqueio) {
  if (!bloqueio) return ''
  const quem = bloqueio.por ? ` por ${bloqueio.por}` : ''
  return bloqueio.em ? `Bloqueado em ${bloqueio.em}${quem}.` : `Bloqueado${quem}.`
}

// Conversa que nasce da massa ou da semente. Padrão seguro: sem o campo
// `bloqueio` e sem a etiqueta antiga, a conversa nasce livre. A etiqueta
// continua sendo lida porque a massa de teste guarda o caso assim.
// Início do atendimento CORRENTE (rodada 5, seção 5): primeira mensagem
// depois do último encerramento em `conversa.atendimentos` (a F5 grava um
// resumo por encerramento ali), ou a primeira mensagem da conversa quando
// não há nenhum ainda. A F5 usa isto para calcular a duração do resumo.
// Fronteira por ÍNDICE, não por tempo (decisão 46, "Encerrar" v2): o relógio
// simulado anda em passos (30 s reais, ou o salto do painel de simulações),
// então a mensagem que reabre pode nascer no MESMO instante gravado que o
// encerramento anterior. Comparar só `em` confundiria as duas. `ateIndice`
// (gravado no resumo por `casos/encerramento.js`) é a posição da última
// mensagem que pertence ao atendimento fechado; tudo com índice maior é do
// atendimento novo, na ordem em que chegou.
// Índice, não tempo: dois eventos do protótipo podem cair no mesmo passo do
// relógio simulado (ele só anda 30 s a cada 30 s reais, ou no salto do
// painel de simulações), então a mensagem que reabre pode nascer com o
// MESMO carimbo gravado do encerramento anterior. `ateIndice` (gravado no
// resumo por `casos/encerramento.js`) é a posição da última mensagem que
// pertence ao atendimento fechado; a próxima é sempre a de índice seguinte,
// não "a próxima com horário maior".
export function indiceInicioDoAtendimento(conversa) {
  const ultimo = (conversa?.atendimentos ?? []).at(-1) ?? null
  return ultimo ? (ultimo.ateIndice ?? -1) + 1 : 0
}

export function atendimentoDesde(conversa) {
  const mensagens = conversa?.mensagens ?? []
  if (mensagens.length === 0) return null
  const indice = indiceInicioDoAtendimento(conversa)
  if (indice === 0) return mensagens[0].em
  return mensagens[indice]?.em ?? conversa.atendimentos.at(-1)?.encerradoEm
}

// Fronteiras do fio (rodada 5, "Encerrar" v2, decisão 46): encerrado nunca
// reabre o MESMO atendimento — a próxima mensagem do cliente começa um
// atendimento novo na mesma conversa. Mesma fronteira por ÍNDICE que
// `atendimentoDesde` usa (ver o comentário lá): por posição na lista de
// mensagens, não por instante, porque dois eventos podem cair no mesmo passo
// do relógio simulado. Uma fronteira por encerramento, na ordem em que
// aconteceram (mesma ordem de `atendimentos`).
export function fronteirasDoFio(atendimentos) {
  return (atendimentos ?? [])
    .filter((a) => a.ateIndice != null)
    .map((a) => ({ numero: a.numero, aposIndice: a.ateIndice }))
}

export function bloqueioDeSemente(conversa) {
  if (conversa.bloqueio) {
    return novoBloqueio({
      motivo: conversa.bloqueio.motivo ?? 'Motivo não registrado.',
      em: conversa.bloqueio.em ?? null,
      por: conversa.bloqueio.por ?? 'Thatiane',
    })
  }
  const tags = conversa.cliente?.tags ?? []
  if (!tags.includes(TAG_BLOQUEADO)) return null
  const nota = (conversa.cliente?.notas ?? []).find((n) => /bloquead/i.test(n.texto))
  return novoBloqueio({
    motivo: nota?.texto ?? 'Bloqueado antes deste registro, sem motivo escrito.',
    em: nota?.em ?? null,
    por: nota?.autor ?? 'Thatiane',
  })
}
