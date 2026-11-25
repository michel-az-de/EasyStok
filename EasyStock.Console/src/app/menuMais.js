// Grupos do menu Mais do topo compacto (#2232, banca UX/UI). Puro, para a prova conferir a ordem, o
// estado do tema e a altura sem montar a tela.
//
//   M1  o tema mostra a opção ativa (`marcado`);
//   M2  a navegação (Módulos, Entregas, Cozinha, Gestão, Ajuda) vem primeiro, separada das preferências;
//   M3  o resumo "Hoje · N em andamento · N na esteira" saiu: quem mostra é o Início;
//   M4  só texto curto nos itens, para a folha inferior do celular caber sem rolagem interna.

import { OPCOES_DE_TEMA, rotuloDoSair } from '../componentes/contaDoMenu'
import { NOME_INICIO } from '../dominio/modulos'

export const CHAVES_DO_MENU = {
  modulos: '__modulos', entregas: '__entregas', cozinha: '__cozinha', gestao: '__gestao', ajuda: '__ajuda',
  claro: '__claro', escuro: '__escuro', sair: '__sair', loja: '__loja', automaticas: '__automaticas',
}

// Medidas da folha, em px: linha de 48 (alvo de toque), 16 a mais quando há detalhe, 32 por título de
// grupo e 16 de recuo em cima e embaixo.
const LINHA = 48
const DETALHE = 16
const TITULO = 32
const RECUO = 16

export function gruposDoMenuMais({
  tema, permite = () => true, aoSair = null, sessaoPersistente = false, atendente = null, casa = null, celular = false, lojaAberta = null, estadoAuto = null,
}) {
  const ir = [
    { chave: CHAVES_DO_MENU.modulos, titulo: NOME_INICIO },
    permite('entregas') && { chave: CHAVES_DO_MENU.entregas, titulo: 'Entregas' },
    // No celular não sobra largura no topo para a Cozinha com nome; ela mora aqui, com a palavra.
    celular && permite('cozinha') && { chave: CHAVES_DO_MENU.cozinha, titulo: 'Cozinha', detalhe: 'Abre em outra janela' },
    { chave: CHAVES_DO_MENU.gestao, titulo: 'Gestão' },
    { chave: CHAVES_DO_MENU.ajuda, titulo: 'Ajuda' },
  ].filter(Boolean)
  return [
    { titulo: 'Ir para', itens: ir },
    // #3282: no celular o topo só leva sino, Mais, filtro e Assistente; a loja e as respostas automáticas
    // moram aqui, lado a lado e sem título visível (uma linha só, para a folha seguir sem rolagem), com o
    // estado no próprio nome.
    // O título "Conta" também fica só para o leitor de tela no celular: os 32 px dele pagam a linha nova.
    ...(celular && lojaAberta !== null ? [{
      titulo: 'Loja',
      tituloOculto: true,
      disposicao: 'linha',
      itens: [
        { chave: CHAVES_DO_MENU.loja, titulo: lojaAberta ? 'Loja aberta' : 'Loja fechada' },
        { chave: CHAVES_DO_MENU.automaticas, titulo: estadoAuto ? `Respostas ${estadoAuto}` : 'Respostas automáticas' },
      ],
    }] : []),
    {
      // #2762: duas linhas de lista, como o grupo de cima; o par lado a lado deixava a opção marcada solta.
      titulo: 'Tema',
      // #2258 (A9/G4): as mesmas opções, na mesma ordem, do menu do avatar (`componentes/contaDoMenu`).
      itens: OPCOES_DE_TEMA.map((opcao) => ({
        chave: opcao.valor === 'dark' ? CHAVES_DO_MENU.escuro : CHAVES_DO_MENU.claro,
        titulo: opcao.rotulo,
        marcado: opcao.valor === 'dark' ? tema === 'dark' : tema !== 'dark',
      })),
    },
    // #1242 (D1): quem está logado e o Sair moram aqui.
    ...(aoSair ? [{
      titulo: 'Conta',
      tituloOculto: celular && lojaAberta !== null,
      itens: [{
        chave: CHAVES_DO_MENU.sair,
        titulo: rotuloDoSair(sessaoPersistente),
        detalhe: [atendente, casa].filter(Boolean).join(' · ') || 'Encerrar a sessão',
      }],
    }] : []),
  ]
}

export const alturaDaFolha = (grupos) => RECUO * 2 + grupos.reduce((soma, grupo) => {
  const linhas = grupo.disposicao === 'linha' ? 1 : grupo.itens.length
  const detalhes = grupo.disposicao === 'linha' ? 0 : grupo.itens.filter((item) => item.detalhe).length
  return soma + (grupo.tituloOculto ? 0 : TITULO) + linhas * LINHA + detalhes * DETALHE
}, 0)
