// Janela de resposta da mensageria. A regra mora aqui, a tela apenas exibe.
// Quem decide se o canal tem janela é o próprio canal, que chega por parâmetro.

import { bloqueioDe, estaBloqueada, resumoDoBloqueio } from './conversa'
import { CANAL_DESCONHECIDO, canalPorNome } from './canal'

export const SEM_JANELA = {
  tom: 'neutro', rotulo: 'Sem janela neste canal', rotuloCurto: 'Sem janela', textoLivre: true,
}

const CANAL_SEM_REGRA = {
  tom: 'aviso', rotulo: 'Canal sem regra cadastrada', rotuloCurto: 'Canal ?', textoLivre: false,
}

const MS_POR_MINUTO = 60000

// A janela nasce da última mensagem do cliente. Quando a conversa traz
// `janelaExpiraEm`, esse valor manda, porque veio da mensageria; senão a conta
// é a regra do canal aplicada à última entrada.
function fimDaJanela(conversa, canal) {
  if (conversa.janelaExpiraEm) return new Date(conversa.janelaExpiraEm).getTime()
  const entradas = conversa.mensagens.filter((m) => m.dir === 'in')
  const ultima = entradas.at(-1)
  if (!ultima) return null
  return new Date(ultima.em).getTime() + canal.horasJanela * 60 * MS_POR_MINUTO
}

export function estadoDaJanela(conversa, agora, canal) {
  const regra = canal ?? CANAL_DESCONHECIDO
  if (regra.desconhecido) return CANAL_SEM_REGRA
  if (!regra.temJanela) return SEM_JANELA

  const fim = fimDaJanela(conversa, regra)
  if (fim == null) {
    return {
      tom: 'aviso', rotulo: 'Janela não iniciada', rotuloCurto: 'Não iniciada', textoLivre: false,
      restam: 0,
    }
  }
  const restam = Math.floor((fim - agora) / MS_POR_MINUTO)
  if (restam <= 0) {
    return {
      tom: 'perigo', rotulo: 'Janela fechada', rotuloCurto: 'Fechada', textoLivre: false, restam: 0,
    }
  }
  const horas = Math.floor(restam / 60)
  const minutos = restam % 60
  const curto = horas > 0 ? `${horas} h ${minutos} min` : `${minutos} min`
  return {
    tom: restam <= 30 ? 'aviso' : 'ok',
    rotulo: `${curto} na janela`,
    rotuloCurto: curto,
    textoLivre: true,
    // Minutos até fechar. Exposto para quem só quer mostrar aviso perto do
    // fim (corte #32 do cartão do Balcão), sem mexer no tom nem no texto que
    // o resto da tela já usa para decidir se pode escrever.
    restam,
  }
}

// Fora da janela, quem não tem modelo aprovado não tem saída nenhuma: o texto
// que a pessoa digitasse cairia calado na mensageria.
function motivoForaDaJanela(janela, canal) {
  if (canal.aceitaModelo) {
    return {
      titulo: `${janela.rotulo}.`,
      detalhe: 'Só modelo aprovado sai até ele responder.',
    }
  }
  return {
    titulo: `${janela.rotulo} no ${canal.nome}.`,
    detalhe: 'Sem modelo aqui. Chame pelo WhatsApp ou espere ele escrever.',
  }
}

// Um único lugar decide se o composer aceita texto livre, se sobra o modelo
// aprovado como saída, e por quê. `ofereceModelo` nasce aqui porque depende das
// MESMAS condições de `pode`: bloqueio, conversa encerrada, canal cadastrado e
// janela. A tela estava recalculando isso por fora, e duas contas para a mesma
// pergunta divergem no dia em que alguém mexe só numa.
export function permissaoDeEscrita(conversa, agora, canal) {
  if (!conversa) return { pode: false, ofereceModelo: false, motivo: null }
  const regra = canal ?? CANAL_DESCONHECIDO
  const janela = estadoDaJanela(conversa, agora, regra)
  // Bloqueio vence tudo, inclusive janela aberta e conversa encerrada.
  if (estaBloqueada(conversa)) {
    const bloqueio = bloqueioDe(conversa)
    return {
      pode: false,
      ofereceModelo: false,
      janela,
      motivo: {
        titulo: 'Cadastro bloqueado.',
        detalhe: `${resumoDoBloqueio(bloqueio)} Motivo: ${bloqueio.motivo} `
          + 'Vale em todos os canais. Desbloqueie na ficha para escrever.',
        acao: 'bloqueio',
      },
    }
  }
  if (conversa.estado === 'Encerrado') {
    return {
      pode: false,
      ofereceModelo: false,
      janela,
      motivo: {
        titulo: 'Conversa encerrada.',
        detalhe: 'Reabra a conversa para voltar a escrever.',
        acao: 'reabrir',
      },
    }
  }
  if (regra.desconhecido) {
    return {
      pode: false,
      ofereceModelo: false,
      janela,
      motivo: {
        titulo: `Canal ${conversa.canal} não está cadastrado.`,
        detalhe: 'Sem a regra do canal, a casa não promete envio aqui.',
      },
    }
  }
  // Fora da janela o modelo aprovado é a única saída, e só existe onde o canal
  // tem modelo. Dentro da janela o texto livre resolve, então não se oferece.
  if (!janela.textoLivre) {
    return {
      pode: false,
      ofereceModelo: regra.aceitaModelo,
      janela,
      motivo: motivoForaDaJanela(janela, regra),
    }
  }
  return { pode: true, ofereceModelo: false, janela, motivo: null }
}

// Encerramento automático (rodada 5, "Encerrar" v2, pedido do dono 24/09):
// conversa aberta cuja janela de resposta fechou (sem mensagem do cliente
// além da janela de 24 h do canal) encerra sozinha. Reaproveita a MESMA conta
// que já barra o composer (`estadoDaJanela`) para decidir quem fecha: tom
// "perigo" só existe quando o canal tem janela, já houve mensagem do
// cliente, e o prazo acabou — as três condições que a regra pede, de graça.
export function conversasParaEncerrarPorJanela(conversas, agora, canais) {
  return (conversas ?? [])
    .filter((c) => c.estado !== 'Encerrado' && !c.reabertaNaMao)
    .filter((c) => estadoDaJanela(c, agora, canalPorNome(canais, c.canal)).tom === 'perigo')
    .map((c) => c.id)
}

// Marca do motivo que nenhum caminho de envio fura. Fora da janela sobra o
// modelo aprovado, conversa encerrada reabre; cadastro bloqueado não tem saída.
export const MOTIVO_BLOQUEIO = 'bloqueio'

// Uma pergunta, uma resposta: "esta conversa pode receber envio?". Quem decide é
// a MESMA função que o composer consulta. Antes o painel do agente e o reducer
// respondiam por conta própria, e mensagem saía para cadastro bloqueado.
export const envioBloqueado = (conversa, agora, canal) =>
  permissaoDeEscrita(conversa, agora, canal).motivo?.acao === MOTIVO_BLOQUEIO
