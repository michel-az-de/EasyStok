import { gavetaDoDia } from '../dominio/caixa'
import { moeda } from '../dominio/formato'

// #2005 (console-02): a aba Caixa fica aberta o dia todo e o "Na gaveta" do modal vinha da carga da
// manhã. Antes de abrir o fechamento, e de novo ao confirmar, o dia é relido: a conta é a do EasyStok
// de agora (a mesma que o esperado gravado nas observações do fechamento).
export const SEM_CONFERIR_O_CAIXA = 'Não deu para conferir o caixa agora. Tente de novo antes de fechar.'

const avisoDoEsperadoQueMudou = (esperado) =>
  `Entrou ou saiu dinheiro enquanto você contava. O esperado na gaveta agora é ${moeda(esperado)}. Confira a contagem e feche de novo.`

// `recarregar` relê o caixa e devolve o dia lido (nulo se falhou). Sem `esperadoVisto` (abertura do modal)
// só garante que a leitura veio. Com ele (confirmação), recusa quando o esperado que ela viu mudou; o modal,
// já com o dia novo, mostra o valor certo para ela conferir.
export async function relerParaFechar(recarregar, { esperadoVisto = null } = {}) {
  const dia = await recarregar()
  if (!dia) return { erro: SEM_CONFERIR_O_CAIXA }
  const gaveta = gavetaDoDia(dia)
  if (esperadoVisto != null && gaveta.naGaveta !== esperadoVisto) return { erro: avisoDoEsperadoQueMudou(gaveta.naGaveta), dia, gaveta }
  return { dia, gaveta }
}
