import { diaMesHora, enderecoDasPartes, mascaraCep, partesDoEndereco } from '../../dominio/formato'
import { favoritoDoCliente } from '../../dominio/ficha'
import { estadoDaSituacao, instante, nomeDoCanal } from './traducaoConversas'

// Cliente da conversa entre o console e o EasyStok (#1276). A Ficha guarda o endereço numa
// string só ("Rua X, 120, apto 12, Bairro, 05500-000"); a API recebe e devolve em partes.

// String da Ficha -> corpo da API. Sem CEP não há endereço para gravar.
export function enderecoParaApi(texto) {
  const { rua, numero, complemento, bairro, cep, cidade, uf } = partesDoEndereco(texto)
  const digitos = cep.replace(/\D/g, '')
  if (!digitos) return null
  return {
    cep: digitos, logradouro: rua || null, numero: numero || null, complemento: complemento || null, bairro: bairro || null,
    ...(cidade ? { cidade } : {}), ...(uf ? { uf } : {}),
  }
}

// Dossiê (S25) -> o que a Ficha mostra. Endereço: o padrão, ou o único.
export function clienteDoDossie(dossie) {
  const dados = dossie?.cliente
  if (!dados?.id) return null
  const enderecos = dossie.enderecos ?? []
  const e = enderecos.find((x) => x.padrao) ?? (enderecos.length === 1 ? enderecos[0] : null)
  const endereco = e
    ? enderecoDasPartes({ rua: e.logradouro, numero: e.numero, complemento: e.complemento, bairro: e.bairro, cep: e.cep ? mascaraCep(e.cep) : '' })
    : null
  return {
    clienteId: dados.id,
    nome: dados.nome || null,
    // #1474 (R2): bloqueio do cadastro, no formato de `dominio/conversa.js` (novoBloqueio). O
    // dossiê não traz quando nem quem bloqueou; `resumoDoBloqueio` lida com os dois nulos.
    bloqueio: dossie.bloqueado
      ? { motivo: dados.motivoBloqueio || 'Sem motivo registrado.', em: null, por: null, alcance: 'todos os canais' }
      : null,
    cliente: {
      telefone: dados.telefone || null,
      email: dados.email || null,
      fotoUrl: dados.fotoUrl ?? null,
      // #2780: conversa ligada a cadastro desativado; a Ficha avisa e leva ao cadastro para reativar.
      inativo: dados.ativo === false,
      // #2734 (C-03): a observação escrita em Cadastros aparece na Ficha, junto das notas.
      observacoes: String(dados.observacoes ?? '').trim() || null,
      endereco: endereco || null,
      pedidos: dossie.totalPedidos ?? 0,
      // #1839: soma e favorito do servidor sobre todas as compras, não só as 10 da lista.
      totalGasto: dossie.totalGasto ?? null,
      // J2 (#1990): só a partir de 2 pedidos com o mesmo item.
      favorito: favoritoDoCliente(dossie.itemFavorito),
      notas: (dossie.notas ?? []).map(notaDaApi),
      // #1441: tags do cadastro (S24), já normalizadas pelo EasyStok.
      tags: (dossie.tags ?? []).map((t) => t.tag),
      // #1436: últimos pedidos no formato do modal Histórico (o mesmo da massa de demonstração).
      // Fica em `cliente` porque a sincronização de 5 s preserva o cliente lido da API.
      historico: (dossie.ultimosPedidos ?? []).map(pedidoDaApi),
    },
  }
}

// #2734: o editor de Cadastros mostra o relacionamento com a mesma leitura da Ficha (`clienteDoDossie`):
// uma tradução só, para a nota, a tag e o bloqueio não divergirem entre as duas telas.
export function relacionamentoDoDossie(dossie) {
  const lido = clienteDoDossie(dossie)
  if (!lido) return null
  const { notas, tags, totalGasto, favorito, pedidos, fotoUrl } = lido.cliente
  return {
    bloqueio: lido.bloqueio, notas, tags, totalGasto, favorito, pedidos, fotoUrl: fotoUrl ?? null,
    conversas: (dossie.conversasRecentes ?? []).map((c) => ({
      id: c.id, canal: nomeDoCanal(c.canal), estado: estadoDaSituacao(c.situacao),
      em: quando(instante(c.ultimaMensagemEm ?? c.iniciadaEm)),
    })),
  }
}

// #1430: o que o visitante do chat do site escreveu no formulário antes de conversar. Vem só no
// dossiê de lead e não é cadastro: a Ficha mostra com selo até a dona confirmar.
export function contatoInformadoDoDossie(dossie) {
  if (dossie?.cliente?.id) return null
  const c = dossie?.contatoInformado
  if (!c?.telefone) return null
  return { nome: c.nome || null, telefone: c.telefone, email: c.email || null, informadoEm: c.informadoEm ?? null }
}

// #2185 (F27): no fuso da casa, não no do aparelho.
const quando = diaMesHora

// `criadoEm` (ISO, UTC) põe a nota no lugar certo do fio como post-it (#1441). Sem o Z o
// navegador leria como hora local e a nota cairia três horas depois.
export const notaDaApi = (n) => ({ id: n.id, texto: n.texto, autor: n.autor, em: quando(instante(n.criadoEm)), criadoEm: instante(n.criadoEm) })

const pedidoDaApi = (p) => ({
  numero: `#${p.id.slice(0, 8)}`,
  em: p.criadoEm.slice(0, 10),
  total: p.total,
  estado: String(p.status ?? '').toLowerCase().replaceAll('_', ' '),
  itens: (p.itens ?? []).map((i) => `${i.quantidade}× ${i.nome}`),
  nota: null,
})
