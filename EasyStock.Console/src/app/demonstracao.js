// Porta das telas do modo demonstração (laboratório local, issue #2017), irmã de `infra/demonstracao.js`:
// com `VITE_FONTE_DADOS=api` o ramo morre no build e nenhuma tela da demonstração entra no bundle. Fora do
// modo API o `await` de módulo carrega `src/demonstracao/telas.js` antes de o App rodar.
import { FONTE_API } from '../infra/fonteDados'

export const telasDaDemonstracao = FONTE_API ? null : await import('../demonstracao/telas.js')
