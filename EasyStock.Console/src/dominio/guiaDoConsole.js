// #1807 (revisão de 14/10): "Ajuda não leva a lugar nenhum útil". A Ajuda do trilho traz o guia
// curto de cada módulo do Console: o que ele faz, os primeiros passos e as telas para abrir. O
// texto fala só do que existe; tela que não está no modo atual não aparece (`telasDoMenu`).
import { moduloDaRota } from './acessoModulos'
import { hashDoModulo, modulosDoHall } from './modulos'

const PASSOS_POR_TELA = {
  cadastros: {
    clientes: 'Em Leads e clientes, busque a pessoa, confira os contatos e volte à conversa quando necessário.',
    pedidos: 'Em Pedidos, ache qualquer pedido pelo número, nome ou telefone e abra para ver itens, pagamentos e histórico.',
    produtos: 'Em Produtos, confira os dados e a situação antes de salvar o cadastro.',
    fornecedores: 'Em Fornecedores, mantenha contatos e condições de compra.',
    bancos: 'Em Contas bancárias, confira os dados da conta antes de salvar ou inativar.',
    pagamentos: 'Em Formas de pagamento, escolha os métodos disponíveis para recebimento.',
    modelos: 'Em Modelos, confira a prévia e aprove a versão para publicá-la na loja.',
  },
  entregas: {
    painel: 'No Painel de entregas, acompanhe os pedidos e as viagens do dia.',
    agenda: 'Na Agenda de encomendas, veja as reservas dos próximos sete dias.',
    janelas: 'Em Janelas de entrega, ajuste horários e capacidade antes de combinar com o cliente.',
  },
  financeiro: {
    caixa: 'No Caixa do dia, confira os lançamentos e os totais.',
    pagar: 'Em Contas a pagar, confira os compromissos da loja.',
    receber: 'Em Contas a receber, acompanhe os valores a receber.',
    relatorios: 'Em Relatórios, escolha o período; Exportar CSV leva os dados para a planilha.',
  },
}

export const GUIA_POR_MODULO = {
  cadastros: {
    faz: 'Pessoas, produtos, fornecedores e dados usados no atendimento e na operação da loja.',
    passos: Object.values(PASSOS_POR_TELA.cadastros),
  },
  atendimento: {
    faz: 'Conversas com os clientes, a ficha de cada um e a comanda do pedido, tudo na tela Conversas.',
    passos: [
      'Em Conversas, escolha o cliente na lista. As conversas que precisam de você aparecem marcadas e tocam um som.',
      'Responda no campo da mensagem; / busca uma resposta pronta. Confira o texto antes de enviar.',
      'No sino de Avisos, use Ativar avisos neste aparelho para saber de contato novo, pagamento e etapa do pedido mesmo com o console fechado.',
    ],
  },
  cardapio: {
    faz: 'Os pratos à venda: fotos, informações, porções, categorias e o que aparece hoje e no site.',
    passos: [
      'Em Itens do cardápio, abra a foto ou o nome do prato para editar.',
      'Hoje liga a oferta do prato no dia; No site mostra o prato ao cliente.',
      'Em Categorias, organize os grupos do cardápio.',
    ],
  },
  producao: {
    faz: 'Estoque e produção do dia, insumos, receitas, planejamento e perdas.',
    passos: [
      'Comece por Insumos: cadastre ingredientes e embalagens com a unidade que você usa.',
      'Em Receitas, ligue cada prato aos insumos e ao rendimento; daí sai o custo por porção.',
      'No dia, registre o que foi feito em Produção do dia e confira o Estoque do dia.',
    ],
  },
  cozinha: {
    faz: 'A fila de preparo dos pedidos do dia, na ordem em que precisam sair.',
    passos: [
      'Confira o dia selecionado antes de começar.',
      'Na Fila de preparo, inicie e conclua cada pedido; o Atendimento vê a etapa na hora.',
      'Se a fila não carregar, use Tentar novamente: lista vazia durante uma falha não quer dizer que não há pedidos.',
    ],
  },
  entregas: {
    faz: 'Acompanhe os pedidos, as viagens e a agenda de entregas da loja.',
    passos: Object.values(PASSOS_POR_TELA.entregas),
  },
  financeiro: {
    faz: 'O caixa e o acompanhamento financeiro da loja.',
    passos: Object.values(PASSOS_POR_TELA.financeiro),
  },
  campanhas: {
    faz: 'Fidelidade e cupons para trazer o cliente de volta.',
    passos: ['Uma função marcada como Em preparação ainda não está disponível.'],
  },
  configuracoes: {
    faz: 'Canais de atendimento, integrações de entrega e impressão.',
    passos: [
      'Em Canais, confira a situação do WhatsApp antes de mexer na configuração.',
      'Em Impressão, escolha o papel de cada documento neste aparelho e o logo da loja.',
    ],
  },
}

// Módulos que o perfil abre no modo atual, com o da tela atual primeiro e aberto.
export function guiaDoConsole({ rota, fonteApi, permite, acoes = null }) {
  const atual = moduloDaRota(rota ?? {})
  const lista = modulosDoHall({ fonteApi, acoes })
    .filter((modulo) => modulo.disponivel && permite(modulo.id) && GUIA_POR_MODULO[modulo.id])
    .map((modulo) => ({
      id: modulo.id,
      nome: modulo.nome,
      icone: modulo.icone,
      ...GUIA_POR_MODULO[modulo.id],
      passos: PASSOS_POR_TELA[modulo.id]
        ? modulo.telas.map((tela) => PASSOS_POR_TELA[modulo.id][tela.id]).filter(Boolean)
        : GUIA_POR_MODULO[modulo.id].passos,
      telas: modulo.telas.map((tela) => ({ id: tela.id, rotulo: tela.rotulo, href: hashDoModulo(modulo.id, tela.id) })),
      aberto: modulo.id === atual,
    }))
  return [...lista.filter((m) => m.aberto), ...lista.filter((m) => !m.aberto)]
}
