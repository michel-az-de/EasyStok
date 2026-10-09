// Resumo do canal WhatsApp na tela Canais (#1447), a partir do
// `GET api/integracoes/whatsapp/status` (que já existia e o console não lia).
// Função pura: o status e o relógio chegam por parâmetro.
//
// `status` null = a API respondeu 404 (atendimento não ligado para a loja);
// `{ semPermissao: true }` = 403 (a rota é só de administrador).
// tom: ok | aviso | neutro.

import { FUSO, dataHora, horaCurta } from './formato'

// "11:50 de 07/10" no fuso da loja. DateTime sem fuso vem da API em UTC.
function horaEDia(valor) {
  const iso = /[zZ]|[+-]\d{2}:\d{2}$/.test(valor) ? valor : `${valor}Z`
  const dia = new Date(iso).toLocaleDateString('pt-BR', { day: '2-digit', month: '2-digit', timeZone: FUSO })
  return `${horaCurta(iso)} de ${dia}`
}

export function resumoDoWhatsApp(status) {
  if (status?.semPermissao) {
    return {
      tom: 'neutro',
      titulo: 'Só o administrador vê o estado do canal',
      linhas: ['Peça para a dona abrir esta tela com o usuário dela.'],
    }
  }
  if (!status) {
    return {
      tom: 'aviso',
      titulo: 'O atendimento por WhatsApp não está ligado para a loja',
      linhas: ['Sem o módulo de atendimento ligado, nenhuma mensagem chega ao Balcão.'],
    }
  }

  // #1474: mensagem que chegou prova que o webhook funciona, mesmo sem a data de verificação
  // gravada; dizer "ainda não verificado" com o Balcão cheio de conversas era mentira.
  const recebendo = Boolean(status.ultimaMensagemRecebidaEm)
  const linhas = [
    status.webhookVerificadoEm
      ? `Webhook verificado em ${dataHora(Date.parse(status.webhookVerificadoEm))}`
      : recebendo ? 'Webhook funcionando: as mensagens chegam ao Balcão' : 'Webhook ainda não verificado pela Meta',
    recebendo
      ? `Recebendo mensagens (última às ${horaEDia(status.ultimaMensagemRecebidaEm)})`
      : 'Nenhuma mensagem recebida ainda',
  ]

  if (!status.phoneNumberId) {
    return { tom: 'aviso', titulo: 'Nenhum número vinculado à loja', linhas }
  }
  if (status.provider === 'stub') {
    return { tom: 'aviso', titulo: 'Canal simulado: as mensagens não saem para a Meta', linhas }
  }
  return { tom: 'ok', titulo: 'WhatsApp ligado', linhas }
}

// Falha do `GET status` em estado da tela: 404 = atendimento não ligado (null),
// 403 = só administrador; qualquer outra (inclusive sem conexão) fica `undefined`
// e a tela mostra o erro, sem fingir que o canal está desligado.
export function statusDaFalha(statusHttp) {
  if (statusHttp === 404) return null
  if (statusHttp === 403) return { semPermissao: true }
  return undefined
}
