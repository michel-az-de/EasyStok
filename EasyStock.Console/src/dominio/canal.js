// Regra de canal. A tabela dos canais mora em infra/catalogo.js e chega aqui
// por parâmetro: o domínio lê o canal, nunca guarda a lista por dentro. Era
// esse acoplamento que fazia Instagram e chat do site herdarem a regra do
// WhatsApp e liberarem texto livre para sempre.

// Padrão seguro para canal que ninguém declarou: não promete janela, não
// promete modelo, não promete anexo. Silencioso é melhor que mentiroso.
export const CANAL_DESCONHECIDO = {
  nome: 'Canal não declarado',
  icone: 'conversa',
  desconhecido: true,
  temJanela: false,
  horasJanela: 0,
  aceitaModelo: false,
  aceitaMidia: {
    foto: false, figurinha: false, audio: false, arquivo: false,
  },
  temFoto: false,
  explicacao: ['Canal sem regra cadastrada.', 'Cadastre o canal para liberar envio.'],
}

export const canalPorNome = (canais, nome) =>
  (canais ?? []).find((c) => c.nome === nome) ?? CANAL_DESCONHECIDO

export const canalDaConversa = (canais, conversa) =>
  (conversa ? canalPorNome(canais, conversa.canal) : CANAL_DESCONHECIDO)

export const aceitaFormato = (canal, formato) => Boolean(canal?.aceitaMidia?.[formato])

// Foto de perfil só aparece quando o canal entrega foto de perfil. Chat do site
// não entrega, e inventar avatar de quem não tem conta é fingir identidade.
export const fotoDoCliente = (canal, cliente) => (canal?.temFoto ? cliente?.foto : null)

import { listaEmPortugues } from './formato'

const NOME_DO_FORMATO = {
  foto: 'foto', figurinha: 'figurinha', audio: 'áudio', arquivo: 'arquivo',
}

// Motivo curto, do tamanho de um botão desabilitado. Quem lê está com a mão na
// massa e precisa entender em três palavras por que aquilo não vai.
export function motivoDeFormato(canal, formato) {
  if (aceitaFormato(canal, formato)) return null
  return `${canal.nome} não aceita ${NOME_DO_FORMATO[formato] ?? formato}.`
}

// #1587: o que o EasyStok envia de verdade no modo API é foto, e só no WhatsApp (o envio de
// mídia da API avisa "ainda não ligado" para o resto). O anexo do composer é recusado ao
// escolher, com o motivo no lugar, em vez de limpar a prévia e avisar no rodapé.
const CANAL_COM_FOTO_NA_API = 'WhatsApp'

export function motivoDeAnexo(canal, tipo, { fonteApi = false } = {}) {
  const formato = tipo === 'imagem' ? 'foto' : 'arquivo'
  const bloqueio = motivoDeFormato(canal, formato)
  if (bloqueio || !fonteApi) return bloqueio
  if (formato === 'foto' && canal.nome === CANAL_COM_FOTO_NA_API) return null
  return `${canal.nome} ainda não recebe ${NOME_DO_FORMATO[formato]} pelo EasyStok.`
}

// Uma linha só com tudo que o canal não aceita, para o pé do composer.
// Integração: só o que o composer oferece hoje. Figurinha saiu no corte (23,
// #54) e continua fora. Rodada 7 (frente Anexos, pedido do dono 24/09):
// anexar arquivo e gravar áudio voltam a ser botões de verdade, então voltam
// para esta lista; "foto" cobre tanto o anexo de imagem quanto o envio pela
// galeria (arte, seção "Prato vira parte da galeria" no registro da frente).
const FORMATOS_DO_COMPOSER = ['foto', 'arquivo', 'audio']

export function restricoesDoCanal(canal) {
  const faltas = FORMATOS_DO_COMPOSER
    .filter((f) => !aceitaFormato(canal, f))
    .map((f) => NOME_DO_FORMATO[f])
  if (faltas.length === 0) return null
  return `${canal.nome} não aceita ${listaEmPortugues(faltas).replace(' e ', ' nem ')}.`
}
