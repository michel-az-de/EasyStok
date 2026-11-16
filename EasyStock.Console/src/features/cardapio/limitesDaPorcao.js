import { algumPassaDoLimite } from '../../componentes/limiteDoCampo'

// #2792: limites do nome e do peso da porção (espelham o servidor), no lugar do `maxLength` que cortava em silêncio.
export const LIMITE_NOME = 60
export const LIMITE_PESO = 40
export const porcoesPassamDoLimite = (linhas) => linhas.some((l) => algumPassaDoLimite([l.rotulo, LIMITE_NOME], [l.peso, LIMITE_PESO]))
