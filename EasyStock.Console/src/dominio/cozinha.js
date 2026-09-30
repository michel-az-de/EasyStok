// Regras da tela de cozinha (US-037, US-038, RN-29 a RN-33, UC-04). Só o que a
// esteira ainda não cobre: quais passos aparecem na cozinha, a cor+rótulo do
// cartão (RN-30) e o atraso calculado (UC-04, exceção E2).

import { PASSOS, motivoParaNaoAvancar, passoPorId, proximoPasso } from './esteira'
import { inicioDaFaixa, janelaPorId, MINUTOS_DE_CORTE } from './entrega'

// A cozinha só vê pedido pago (o canhoto já saiu, RN-27) e ainda não
// cancelado: "aguardando" é balcão, não esteira de produção (UC-04,
// pré-condição "pedido pago e impresso"). Cancelado nunca esteve em PASSOS.
export const PASSOS_DA_COZINHA = PASSOS.filter((p) => p.id !== 'aguardando')

const IDS_DA_COZINHA = new Set(PASSOS_DA_COZINHA.map((p) => p.id))

export const pedidosNaCozinha = (conversas) => conversas.filter(
  (c) => c.pedido && IDS_DA_COZINHA.has(c.pedido.estado),
)

// "Horário de início de preparo calculado" (US-037, critério de aceite;
// UC-04 E2). Não existe constante de "tempo de preparo" no catálogo: em vez
// de inventar um número novo, reaproveita o corte da própria janela
// (dominio/entrega.js, "tempo mínimo entre anotar e ter a massa embalada"),
// que já é o intervalo usado para decidir se dá tempo de preparar antes da
// janela. Rodada 13 (issue #42): corte passou a poder variar por janela; sem
// o campo (semente antiga) cai no chão de sempre, `MINUTOS_DE_CORTE`.
export function horarioInicioPreparo(pedido, janelas, agora) {
  const janela = janelaPorId(janelas, pedido?.janela)
  const inicioJanela = janela?.faixa ? inicioDaFaixa(janela.faixa, agora) : null
  return inicioJanela == null ? null : inicioJanela - (janela?.corteMinutos ?? MINUTOS_DE_CORTE) * 60000
}

// Atraso só existe enquanto o preparo não começou (UC-04 E2: "entre os
// passos 3 e 4"). Depois que ela toca "iniciar preparo", o pedido sai do
// risco de atraso de início.
export function pedidoAtrasado(pedido, janelas, agora) {
  if (pedido?.estado !== 'pago') return false
  const horario = horarioInicioPreparo(pedido, janelas, agora)
  return horario != null && agora > horario
}

// Cor + rótulo do cartão (RN-30: cor nunca decide sozinha, o rótulo escrito
// vai sempre junto). UC-04 E2: o card só muda para o estado de atraso quando
// o horário de início calculado passa — antes disso "pago" é espera normal,
// não alarme. 'esperando' fica em tom neutro (--info, mesma aposentadoria
// que os outros neutros da tela); só 'atraso' usa a cor de perigo (--parado,
// apelidada --perigo em tokens.css). Entregue mantém o azul próprio (nota em
// tokens.css: "ela quer azul aqui, não neutro").
export function situacaoDoCartao(pedido, janelas, agora) {
  if (pedidoAtrasado(pedido, janelas, agora)) return { chave: 'atraso', rotulo: 'Atrasado' }
  if (pedido.estado === 'pago') return { chave: 'esperando', rotulo: 'Esperando preparo' }
  if (pedido.estado === 'entregue') return { chave: 'entregue', rotulo: 'Entregue' }
  // preparo, embalado, entrega: mesma cor (verde, "em preparo" no sentido
  // amplo que ela usou), rótulo exato do passo para não mentir qual dos três.
  return { chave: 'preparo', rotulo: passoPorId(pedido.estado)?.rotulo ?? 'Em preparo' }
}

// Ícone por passo (direção visual, seção 5). Mesmo conteúdo de
// `ICONE_DA_TRILHA` em `features/ficha-cliente/BarraProximoPasso.jsx`,
// duplicado de propósito: feature nenhuma importa outra feature
// (`ferramentas/verificar-camadas.mjs`), e são 5 linhas de dado, não lógica.
export const ICONE_DO_PASSO = {
  pago: 'circle-check', preparo: 'cooking-pot', embalado: 'package', entrega: 'moto', entregue: 'house',
}

// Rótulo do botão de avançar (RN-31, um toque só). Mesmo texto de
// `ACAO_DO_PASSO` em BarraProximoPasso.jsx, duplicado pelo mesmo motivo acima.
export const ROTULO_DO_TOQUE = {
  pago: 'Iniciar preparo', preparo: 'Marcar embalado', embalado: 'Despachar pedido', entrega: 'Marcar entregue',
}

// Arrastar e soltar entre colunas (issue #7, rodada 11: "cozinha deveria ter
// drag and drop com efeitos visuais"). Soltar é o MESMO toque do botão de
// avançar (RN-31), então só aceita a coluna do próximo passo: pular ou voltar
// passo pelo arrasto seria uma porta que o botão não abre. Cliente bloqueado
// recusa com o mesmo motivo do botão desabilitado (RN-14, `motivoParaNaoAvancar`,
// a mesma resposta que o reducer usa). Soltar na própria coluna não é recusa:
// o cartão só volta para onde estava, sem aviso.
export function respostaAoSoltar(atual, destino, { bloqueado }) {
  if (destino === atual) return { aceita: false, motivo: null }
  const proximo = proximoPasso(atual)
  if (!proximo) return { aceita: false, motivo: 'Pedido entregue não anda mais na esteira.' }
  if (destino !== proximo.id) {
    return { aceita: false, motivo: `Um passo por vez: daqui o pedido só vai para ${proximo.rotulo}.` }
  }
  const motivo = motivoParaNaoAvancar(destino, { bloqueado })
  return motivo ? { aceita: false, motivo } : { aceita: true, motivo: null }
}
