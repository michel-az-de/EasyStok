// #1807 (revisão de 14/10): "Ajuda não leva a lugar nenhum útil". A Ajuda do trilho traz o guia
// curto de cada módulo do Console: o que ele faz, os primeiros passos e as telas para abrir. O
// texto fala só do que existe; tela que não está no modo atual não aparece (`telasDoMenu`).
import { moduloDaRota } from './acessoModulos'
import { hashDoModulo, modulosDoHall } from './modulos'

export const GUIA_POR_MODULO = {
  atendimento: {
    faz: 'Conversas com os clientes, a ficha de cada um e a comanda do pedido, tudo no Balcão.',
    passos: [
      'No Balcão, escolha a conversa na lista. As que precisam de você aparecem marcadas e tocam um som.',
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
      'Na Fila de preparo, inicie e conclua cada pedido; o Balcão vê a etapa na hora.',
      'Se a fila não carregar, use Tentar novamente: lista vazia durante uma falha não quer dizer que não há pedidos.',
    ],
  },
  entregas: {
    faz: 'Os pedidos a entregar, as viagens, a agenda de encomendas e as janelas de entrega.',
    passos: [
      'No Painel de entregas, acompanhe os pedidos e as viagens do dia.',
      'Na Agenda de encomendas, veja as reservas dos próximos sete dias.',
      'Em Janelas de entrega, ajuste horários e capacidade antes de combinar com o cliente.',
    ],
  },
  // #1891: o financeiro gerencial está congelado (dominio/congelados.js); o guia fala só do Caixa do dia.
  financeiro: {
    faz: 'O Caixa do dia: saldo inicial, entradas e saídas, e o fechamento no fim do expediente.',
    passos: [
      'Abra o Caixa do dia e confira o saldo e os lançamentos.',
      'Use Lançar entrada ou saída para registrar o que entrou e saiu fora dos pedidos.',
      'No fim do expediente, use Fechar o caixa e confira o saldo do dia.',
    ],
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
export function guiaDoConsole({ rota, fonteApi, permite }) {
  const atual = moduloDaRota(rota ?? {})
  const lista = modulosDoHall({ fonteApi })
    .filter((modulo) => modulo.disponivel && permite(modulo.id) && GUIA_POR_MODULO[modulo.id])
    .map((modulo) => ({
      id: modulo.id,
      nome: modulo.nome,
      icone: modulo.icone,
      ...GUIA_POR_MODULO[modulo.id],
      telas: modulo.telas.map((tela) => ({ id: tela.id, rotulo: tela.rotulo, href: hashDoModulo(modulo.id, tela.id) })),
      aberto: modulo.id === atual,
    }))
  return [...lista.filter((m) => m.aberto), ...lista.filter((m) => !m.aberto)]
}
