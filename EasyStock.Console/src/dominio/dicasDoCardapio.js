// #1808: explicação curta de cada controle da lista de Itens e a dica estática do prato. Os códigos
// de pendência vêm do servidor (CardapioItem.PendenciasDeApresentacao); o texto espelha a dica
// estática do AssistenteDoCardapioUseCase, que é o que a IA devolve quando não está configurada.
export const DESCRICAO_MINIMA = 40

const TEXTO_DA_PENDENCIA = {
  SemFoto: 'adicione uma foto do prato pronto',
  SemCategoria: 'escolha uma categoria para o cliente achar o prato',
  SemDescricao: 'escreva uma descrição com o que vem no prato',
  DescricaoCurta: `aumente a descrição para pelo menos ${DESCRICAO_MINIMA} caracteres`,
}

export function dicaEstatica(pendencias) {
  const partes = (pendencias ?? []).map((p) => TEXTO_DA_PENDENCIA[p]).filter(Boolean)
  if (!partes.length) return ''
  const texto = partes.join('; ') + '.'
  return texto[0].toLocaleUpperCase('pt-BR') + texto.slice(1)
}

export const EXPLICACAO_DOS_CONTROLES = {
  filtros: 'Mostra só os pratos de hoje, os desligados ou os que ainda não têm foto.',
  categoria: 'Mostra só os pratos de uma categoria; combina com o filtro e com a busca.',
  hoje: 'Ligado, o prato pode ser oferecido hoje. Desligado, só volta quando você religar.',
  noSite: 'Controla se o cliente vê o prato no site. Oculto continua no cadastro.',
  arquivar: 'Tira o prato do cardápio sem apagar. Você pode repor depois.',
  validar: 'Libera o prato novo para o assistente oferecer aos clientes.',
  ordem: 'Sobe ou desce o prato na ordem do cardápio do site.',
  foto: 'Abre a ficha do prato na parte da foto para adicionar ou trocar.',
  editar: 'Abre a ficha do prato: nome, preço, porções, categoria e descrição.',
  maisAcoes: 'Site, arquivar, validar e ordem do prato.',
}
