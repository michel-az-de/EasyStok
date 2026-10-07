// Resumo do canal WhatsApp na tela Canais (#1447), a partir do
// `GET api/integracoes/whatsapp/status` (que já existia e o console não lia).
// Função pura: o status e o relógio chegam por parâmetro.
//
// `status` null = a API respondeu 404 (atendimento não ligado para a loja);
// `{ semPermissao: true }` = 403 (a rota é só de administrador).
// tom: ok | aviso | neutro.

import { dataHora, haQuantoTempo } from './formato'

export function resumoDoWhatsApp(status, agora) {
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

  const linhas = [
    status.webhookVerificadoEm
      ? `Webhook verificado em ${dataHora(Date.parse(status.webhookVerificadoEm))}`
      : 'Webhook ainda não verificado pela Meta',
    status.ultimaMensagemRecebidaEm
      ? `Última mensagem recebida há ${haQuantoTempo(status.ultimaMensagemRecebidaEm, agora)}`
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
