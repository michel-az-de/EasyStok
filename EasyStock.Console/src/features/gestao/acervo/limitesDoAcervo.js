import { algumPassaDoLimite } from '../../../componentes/limiteDoCampo'

// #2792: para travar o envio de um formulário do acervo; `limites` é { nomeDoCampo: N } (N espelha o servidor).
export function algumCampoPassaDoLimite(formulario, limites) {
  const dados = new FormData(formulario)
  return algumPassaDoLimite(...Object.entries(limites).map(([nome, limite]) => [dados.get(nome), limite]))
}
