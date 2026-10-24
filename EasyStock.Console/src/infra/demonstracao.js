// Porta do modo demonstração (decisão do Felipe, 24/10, issue #2017): a demonstração é laboratório local e
// não vai no build de produção. Aqui, e só aqui, o código dela é carregado, por import dinâmico atrás de um
// ramo que o build elimina: com `VITE_FONTE_DADOS=api` o `FONTE_API` vira `true`, o ramo morre e nenhum
// módulo de `src/demonstracao/` (nem o que só ele importa) entra no bundle. Fora do modo API (desenvolvimento,
// imagem de demonstração, provas) o `await` de módulo carrega o pacote antes de qualquer importador rodar.
//
// Regra de ouro: nada do que `src/demonstracao/dados.js` importa pode importar este arquivo (nem módulo que
// o importe), senão o `await` espera por si mesmo. A prova `prova-2017-demonstracao-fora-do-bundle.mjs`
// confere o ciclo e o bundle.
import { FONTE_API } from './fonteDados'

export const demonstracao = FONTE_API ? null : await import('../demonstracao/dados.js')
