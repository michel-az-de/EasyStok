/* eslint-disable no-console */
// Prova da #2543 (Q13 da auditoria QA de 13/11, `docs/dev/revisoes/2026-11-13-qa-console-web.md`, pacote 5).
//
// O chip do motivo no cartão da lista cortava em 28 letras qualquer motivo que a API gravou: "Contato
// encaminhado à…", "Contato da equipe que recebe…". Agora cada motivo fixo da API tem rótulo curto,
// sem reticências; a frase inteira continua na barra da conversa. A prova liga as pontas: cada caso
// confere que o trecho ainda existe no C#, e a contagem de pontos de escalada falha quando nasce um novo.
//
//   node ferramentas/prova-2543-chip-do-motivo.mjs

import { readFileSync } from 'node:fs'
import { registerHooks } from 'node:module'
import assert from 'node:assert/strict'

registerHooks({
  resolve(especificador, contexto, proximo) {
    if (especificador.startsWith('.') && !/\.[a-z]+$/i.test(especificador)) {
      try { return proximo(especificador + '.js', contexto) } catch { /* cai no original */ }
    }
    return proximo(especificador, contexto)
  },
})

let ok = 0
const falhas = []
const caso = (nome, fn) => {
  try { fn(); ok++; console.log(`ok  ${nome}`) } catch (erro) { falhas.push(nome); console.log(`FALHA  ${nome}\n      ${String(erro.message).split('\n')[0]}`) }
}

const { origemDaPassagem } = await import('../src/dominio/passagem.js')
const { motivoDeEscalada } = await import('../src/dominio/motivoDeEscalada.js')

const raiz = new URL('../../', import.meta.url)
const cs = (caminho) => readFileSync(new URL(caminho, raiz), 'utf8')
// Mesmo caminho do modo API: `traducaoConversas` passa o motivo por `motivoDeEscalada` antes da passagem.
const chip = (gravado) => origemDaPassagem({ origem: 'agente', motivo: motivoDeEscalada(gravado) }).chip

const APP = 'EasyStock.Application/'
const ATD = `${APP}Services/Atendimento/`
const UC = `${APP}UseCases/Atendimento/`

// [arquivo C#, trecho que precisa existir nele, motivo como a API grava, chip esperado]
const MOTIVOS = [
  [`${UC}EscalarConversaUseCase.cs`, '"contato encaminhado à responsável"',
    'contato encaminhado à responsável (pediu orçamento); o robô volta quando a equipe devolver a conversa', 'Com a responsável'],
  [`${ATD}Ferramentas/EncaminharParaResponsavelFerramenta.cs`, '"encaminhado à responsável: "',
    'encaminhado à responsável: orçamento de bolo de casamento', 'Com a responsável'],
  [`${ATD}AgenteAtendimentoService.cs`, '"contato da equipe que recebe os avisos: o robô não responde"',
    'contato da equipe que recebe os avisos: o robô não responde', 'Contato da equipe'],
  [`${UC}EscalarConversaUseCase.cs`, '$"cliente bloqueado: {cliente.MotivoBloqueio}"',
    'cliente bloqueado: não pagou o último pedido', 'Cliente bloqueado'],
  [`${ATD}AgenteAtendimentoService.cs`, '$"o robô respondeu {maximo} vezes nesta conversa na última hora;',
    'o robô respondeu 20 vezes nesta conversa na última hora; parou para não gastar sem limite (possível loop)', 'Limite de respostas'],
  [`${UC}ProcessarTurnoAgenteUseCase.cs`, '"o Assistente falhou ao responder;',
    'o Assistente falhou ao responder; leia a mensagem do cliente e responda você', 'Assistente falhou'],
  [`${ATD}GeracaoAgenteAtendimento.cs`, '"o agente não conseguiu responder (falha ao chamar o LLM)"',
    'o agente não conseguiu responder (falha ao chamar o LLM)', 'Assistente falhou'],
  [`${ATD}GeracaoAgenteAtendimento.cs`, '$"o agente não produziu resposta (stop_reason=',
    'o agente não produziu resposta (stop_reason=max_tokens)', 'Assistente sem resposta'],
  [`${ATD}AgenteAtendimentoService.cs`, '$"resposta do agente barrada ({barrada}); precisa de conferência humana"',
    'resposta do agente barrada (preço fora da tabela); precisa de conferência humana', 'Resposta para conferir'],
  [`${ATD}AgenteAtendimentoService.cs`, '$"o agente atingiu o limite de {MaximoIteracoes} iterações',
    'o agente atingiu o limite de 8 iterações sem concluir a resposta', 'Assistente não concluiu'],
  [`${ATD}AgenteAtendimentoService.cs`, '"o menu de janelas não saiu ao cliente;',
    'o menu de janelas não saiu ao cliente; precisa de conferência humana', 'Menu não enviado'],
  [`${ATD}PortoesDoAssistente.cs`, '$"o canal {conversa.Canal} não tem agente"',
    'o canal Instagram não tem agente', 'Canal sem Assistente'],
  [`${ATD}AgenteAtendimentoService.cs`, '"agente desligado (LLM indisponível)"',
    'agente desligado (LLM indisponível)', 'Sem automático'],
  [`${ATD}AgenteAtendimentoService.cs`, '"atendimento automático desligado na configuração"',
    'atendimento automático desligado na configuração', 'Sem automático'],
  [`${ATD}AcoesBotao/AvaliacaoAcaoBotao.cs`, '"cliente avaliou o pedido como negativo"',
    'cliente avaliou o pedido como negativo', 'Avaliação negativa'],
  [`${ATD}AcoesBotao/AvaliacaoAcaoBotao.cs`, '"botão de avaliação sem pedido reconhecido"',
    'botão de avaliação sem pedido reconhecido', 'Avaliação a conferir'],
  [`${ATD}AcoesBotao/AvaliacaoAcaoBotao.cs`, '"botão de avaliação para pedido não elegível"',
    'botão de avaliação para pedido não elegível', 'Avaliação a conferir'],
  [`${ATD}AcoesBotao/ConfirmarEnderecoAcaoBotao.cs`, '$"cliente confirmou o endereço {payload} pelo botão, mas não foi possível gravar',
    'cliente confirmou o endereço Rua das Flores, 10 pelo botão, mas não foi possível gravar (timeout)', 'Endereço não gravado'],
  [`${UC}Webhook/ProcessarEventoWhatsAppUseCase.cs`, '"Telefone compartilhado: confira a pessoa',
    'Telefone compartilhado: confira a pessoa e escolha o cadastro antes de continuar.', 'Telefone compartilhado'],
  [`${UC}Comanda/ComandaClienteUseCase.cs`, '"Endereço pendente. Peça o endereço',
    'Endereço pendente. Peça o endereço ao cliente e combine a entrega e o frete.', 'Endereço pendente'],
  [`${UC}Comanda/ComandaClienteUseCase.cs`, '"Entrega e frete a combinar.',
    'Entrega e frete a combinar. Confira o endereço e a seleção da comanda.', 'Entrega e frete a combinar'],
  [`${UC}Comanda/ComandaClienteUseCase.cs`, '"Cliente pediu ajuda pela comanda.',
    'Cliente pediu ajuda pela comanda. Confira a conversa e a seleção antes de continuar.', 'Ajuda na comanda'],
  [`${UC}Ocorrencias/AbrirOcorrenciaUseCase.cs`, '$"ocorrência aberta ({OcorrenciaDto.Snake(',
    'ocorrência aberta (atraso): o bolo não chegou', 'Ocorrência aberta'],
  ['EasyStock.Domain/Entities/Atendimento/Conversa.cs', '"Conversa reaberta para atendimento."',
    'Conversa reaberta para atendimento.', 'Conversa reaberta'],
]

for (const [arquivo, trecho, gravado, esperado] of MOTIVOS) {
  caso(`${esperado} ← ${gravado.slice(0, 48)}`, () => {
    assert.ok(cs(arquivo).includes(trecho), `o trecho ${trecho} sumiu de ${arquivo}: atualize o mapa em motivoDeEscalada.js`)
    const rotulo = chip(gravado)
    assert.equal(rotulo, esperado)
    assert.ok(rotulo.length <= 28 && !rotulo.includes('…'), `chip longo ou cortado: ${rotulo}`)
  })
}

caso('motivo livre (ferramenta de escalar do Assistente) segue o corte em 28 letras', () => {
  assert.equal(chip('Restrição alimentar, cliente celíaca'), 'Restrição alimentar')
  assert.equal(chip('cliente quer mudar o sabor do bolo de aniversário da filha'), 'Cliente quer mudar o sabor…')
})

// Ponto de escalada novo na API = motivo novo sem rótulo. Conte e mapeie antes de mudar o número.
caso('nenhum ponto de escalada novo na API sem rótulo curto', () => {
  const { execSync } = process.getBuiltinModule('node:child_process')
  const saida = execSync('git grep -c -E "EscalarAsync\\(|\\.Escalar\\(" -- "EasyStock.Application/*.cs" "EasyStock.Domain/*.cs"',
    { cwd: new URL(raiz).pathname.replace(/^\/([A-Za-z]:)/, '$1'), encoding: 'utf8' })
  const total = saida.trim().split('\n').reduce((soma, linha) => soma + Number(linha.split(':').pop()), 0)
  assert.equal(total, 22, 'mudou o número de chamadas de escalada: mapeie o motivo novo em motivoDeEscalada.js e ajuste este número')
})

console.log(`\n${ok} ok, ${falhas.length} falha(s)`)
if (falhas.length) process.exit(1)
