import massa from '../../dados/massa-conversas.json'
import { INSTANTE_INICIAL } from './catalogo'
import { CONVERSAS_GERADAS } from './massaClientesGerados'
import { carregarHistorico } from './historicoPedidos'
import { gerarAtendimentosAntigos } from './atendimentosAntigos'

// Massa de conversas com o resultado esperado do agente em cada uma
// (dados/massa-conversas.json). O arquivo guarda tempo em minutos relativos ao
// instante inicial da tela; aqui vira instante ISO, a forma que a tela usa.
//
// Rodada 10 (achados P1.1, P1.3, P2, "o que falta construir" 1 e 4): três
// acréscimos por cima da conversão de sempre, na mesma função, para nenhuma
// outra camada precisar saber que existe geração de massa por trás.
//   CONVERSAS_GERADAS   dezenas de cadastros novos (massaClientesGerados.js),
//                       juntados aos 31 de sempre antes de qualquer conversão.
//   historico           anexado em toda conversa (`conversa.historico`), para
//                       `combinaBusca` (dominio/conversa.js) casar a busca do
//                       Balcão também contra o histórico de compra.
//   atendimentos antigos prepostos ao fio de cada cliente com 2+ pedidos em
//                       meses diferentes (atendimentosAntigos.js), com
//                       numeração AT sequencial na loja inteira (mesma regra
//                       de `casos/encerramento.js`, só que somada aqui uma
//                       vez só, por data, no lugar de crescer a cada clique).

const base = new Date(INSTANTE_INICIAL).getTime()
const antes = (minutos) => new Date(base - minutos * 60000).toISOString()
const depois = (minutos) => new Date(base + minutos * 60000).toISOString()

// `pedido.cobranca` (dominio/cobranca.js) guarda `criadaEm`/`expiraEm` como
// instante numérico (mesma unidade de `agora`, não ISO), porque é contra esse
// número que `situacaoDaCobranca` faz conta direto. Sem esta conversão a massa
// ficaria sem cobrança real e o Balcão (que aceita pedido sem Pix, prazo pela
// última mensagem) e a ficha (que só lê `pedido.cobranca`) descreviam a mesma
// cobrança vencida de formas diferentes.
const antesNum = (minutos) => base - minutos * 60000
const depoisNum = (minutos) => base + minutos * 60000

// `pagaMinutosAtras` é opcional: só a massa que precisa de um pedido JÁ pago
// (para oferecer estorno de verdade, UC-06) o declara. Sem ele, `pagaEm` segue
// o que já vinha no JSON (null nos pedidos ainda aguardando).
function converterCobranca(cobranca) {
  if (!cobranca) return null
  const { criadaMinutosAtras, expiraDaquiMinutos, pagaMinutosAtras, ...resto } = cobranca
  const convertida = { ...resto, criadaEm: antesNum(criadaMinutosAtras), expiraEm: depoisNum(expiraDaquiMinutos) }
  return pagaMinutosAtras == null ? convertida : { ...convertida, pagaEm: antesNum(pagaMinutosAtras) }
}

// Numera os atendimentos antigos de TODA a loja em ordem cronológica
// (AT-0001, AT-0002...), do jeito que a numeração ao vivo já funciona em
// `casos/encerramento.js` ("sequencial, contando quantos resumos já existem
// em QUALQUER conversa"). Sem este passo cada conversa recomeçaria a
// numeração do zero, e duas AT-0001 diferentes apareceriam na mesma loja.
function numerarAtendimentosAntigos(conversas) {
  const todos = conversas.flatMap((c) => c.atendimentos ?? [])
  todos.sort((a, b) => a.encerradoEm - b.encerradoEm)
  todos.forEach((atendimento, indice) => {
    atendimento.numero = `AT-${String(indice + 1).padStart(4, '0')}`
  })
  return conversas
}

export function carregarMassa() {
  const convertidas = [...massa.conversas, ...CONVERSAS_GERADAS]
    .map(({ janelaMinutos, ultimaMinutosAtras, mensagens, ...conversa }) => ({
      ...conversa,
      // Rodada 5 (seção 2): cadastroId no lugar do nome em mesmoCadastro. Massa
      // já vem com o campo; o `?? id` é só rede de segurança para conversa
      // simulada que a F7 injetar sem passar por este arquivo.
      cadastroId: conversa.cadastroId ?? conversa.id,
      janelaExpiraEm: janelaMinutos == null ? null : depois(janelaMinutos),
      ultimaEm: antes(ultimaMinutosAtras),
      mensagens: mensagens.map(({ minutosAtras, ...m }) => ({ ...m, em: antes(minutosAtras) })),
      pedido: conversa.pedido
        ? { ...conversa.pedido, cobranca: converterCobranca(conversa.pedido.cobranca) }
        : conversa.pedido,
    }))

  const comHistoricoEAtendimentos = convertidas.map((conversa) => {
    const historico = carregarHistorico(conversa.id)
    const { mensagens: mensagensAntigas, atendimentos } = gerarAtendimentosAntigos({
      id: conversa.id,
      nome: conversa.nome,
      historico,
      pedidoVivoNumero: conversa.pedido?.numero ?? null,
    })
    return {
      ...conversa,
      historico,
      mensagens: [...mensagensAntigas, ...conversa.mensagens],
      atendimentos: [...(conversa.atendimentos ?? []), ...atendimentos],
    }
  })

  return numerarAtendimentosAntigos(comHistoricoEAtendimentos)
}

export const TOTAL_MASSA = massa.conversas.length + CONVERSAS_GERADAS.length
export const VERSAO_MASSA = massa.versao
