import { chamarApi } from './cliente'

// #2813 (B4): os fechamentos de caixa que o aparelho antigo guardou no backup, SÓ PARA LER (LegacyCashClosingsController,
// policy Gerente, empresa do token). A resposta vem direto como `{ itens, ignorados }`, fora do envelope `{ data }`.
// Não há escrita nem reenvio: o aparelho nunca mais sobe fechamento (#2030), então este arquivo só tem a leitura.
export const listarFechamentosLegados = (sinal) =>
  chamarApi('/api/mobile/legacy-import/fechamentos', { envelope: true, sinal })
