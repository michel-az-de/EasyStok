// Grupos do menu Mais do topo compacto (#2232, banca UX/UI). Puro, para a prova conferir a ordem, o
// estado do tema e a altura sem montar a tela.
//
//   M1  o tema mostra a opção ativa (`marcado`);
//   M2  a navegação (Módulos, Entregas, Cozinha, Gestão, Ajuda) vem primeiro, separada das preferências;
//   M3  o resumo "Hoje · N em andamento · N na esteira" saiu: quem mostra é o Início;
//   M4  só texto curto nos itens, para a folha inferior do celular caber sem rolagem interna.

export const CHAVES_DO_MENU = {
  modulos: '__modulos', entregas: '__entregas', cozinha: '__cozinha', gestao: '__gestao', ajuda: '__ajuda',
  claro: '__claro', escuro: '__escuro', sair: '__sair',
}

// Medidas da folha, em px: linha de 48 (alvo de toque), 16 a mais quando há detalhe, 32 por título de
// grupo e 16 de recuo em cima e embaixo.
const LINHA = 48
const DETALHE = 16
const TITULO = 32
const RECUO = 16

export function gruposDoMenuMais({
  tema, permite = () => true, aoSair = null, sessaoPersistente = false, atendente = null, casa = null, celular = false,
}) {
  const ir = [
    { chave: CHAVES_DO_MENU.modulos, titulo: 'Módulos' },
    permite('entregas') && { chave: CHAVES_DO_MENU.entregas, titulo: 'Entregas' },
    // No celular não sobra largura no topo para a Cozinha com nome; ela mora aqui, com a palavra.
    celular && permite('cozinha') && { chave: CHAVES_DO_MENU.cozinha, titulo: 'Cozinha', detalhe: 'Abre em outra janela' },
    { chave: CHAVES_DO_MENU.gestao, titulo: 'Gestão' },
    { chave: CHAVES_DO_MENU.ajuda, titulo: 'Ajuda do sistema' },
  ].filter(Boolean)
  return [
    { titulo: 'Ir para', itens: ir },
    {
      titulo: 'Tema',
      disposicao: 'linha',
      itens: [
        { chave: CHAVES_DO_MENU.claro, titulo: 'Claro', marcado: tema !== 'dark' },
        { chave: CHAVES_DO_MENU.escuro, titulo: 'Escuro', marcado: tema === 'dark' },
      ],
    },
    // #1242 (D1): quem está logado e o Sair moram aqui.
    ...(aoSair ? [{
      titulo: 'Conta',
      itens: [{
        chave: CHAVES_DO_MENU.sair,
        titulo: sessaoPersistente ? 'Sair deste aparelho' : 'Sair',
        detalhe: [atendente, casa].filter(Boolean).join(' · ') || 'Encerrar a sessão',
      }],
    }] : []),
  ]
}

export const alturaDaFolha = (grupos) => RECUO * 2 + grupos.reduce((soma, grupo) => {
  const linhas = grupo.disposicao === 'linha' ? 1 : grupo.itens.length
  const detalhes = grupo.disposicao === 'linha' ? 0 : grupo.itens.filter((item) => item.detalhe).length
  return soma + TITULO + linhas * LINHA + detalhes * DETALHE
}, 0)
