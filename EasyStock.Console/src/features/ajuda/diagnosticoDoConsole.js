import { moduloDaRota } from '../../dominio/acessoModulos'
import { moduloPorId, telasDoMenu } from '../../dominio/modulos'

const GUIAS = {
  atendimento: [
    'Escolha a conversa no Balcão. Atendimento reúne as mensagens; a Ficha guarda os dados do cliente e a comanda.',
    'Use Respostas para inserir um texto pronto. Confira o destinatário e o texto antes de enviar.',
    'Peça sugestões ao Assistente. Revise cada proposta antes de usar ou enviar ao cliente.',
  ],
  cardapio: [
    'Abra a foto ou o nome do prato para editar fotos, informações e porções, conforme seu acesso.',
    'Hoje controla a oferta do prato no dia. No site controla a exibição para o cliente. Um prato em validação precisa ser validado para o assistente oferecê-lo.',
  ],
  producao: [
    'Estoque do dia mostra o que há disponível; Produção do dia registra o que foi preparado.',
    'Insumos e Receitas relacionam ingredientes, embalagens e rendimento. Confira a unidade e a quantidade antes de salvar.',
  ],
  cozinha: [
    'Confira o dia selecionado antes de iniciar ou concluir um preparo.',
    'Se a fila não carregar, use Tentar novamente na própria tela. A ausência de dados durante uma falha não significa que não há pedidos.',
  ],
  entregas: [
    'No Painel de entregas, acompanhe os pedidos e viagens. Janelas de entrega reúne horários e capacidade.',
    'Depois de uma falha, confira se a ação já foi confirmada antes de repeti-la. Os campos preservados permitem corrigir e tentar de novo.',
  ],
  financeiro: [
    'Confira os valores e lançamentos no Caixa do dia antes de fechar.',
    'As ações disponíveis dependem do seu perfil. Se faltar uma ação necessária, peça à dona para revisar seu acesso.',
  ],
  campanhas: [
    'Uma função marcada como Em preparação ainda não está disponível.',
  ],
  configuracoes: [
    'Canais reúne a situação do WhatsApp. Confira os avisos antes de alterar a configuração.',
    'Uma função marcada como Em preparação ainda não está disponível.',
  ],
}

const TELAS = {
  'atendimento/horarios': ['Use Ajustar horários da semana para conferir o expediente e Salvar horários para confirmar. As mudanças valem para o atendimento da empresa.', 'Mensagens e ajustes do automático também precisam de Salvar. Um rascunho fica mantido nesta sessão ao trocar de tela; use Desfazer edição na seção desejada para recuperar os valores salvos.'],
  'atendimento/respostas': ['Crie ou edite respostas prontas com título, atalho e texto. Use a busca para encontrar uma mensagem.', 'As automáticas têm texto e estado próprios. Confira a prévia antes de ligá-las.'],
  'cardapio/categorias': ['Organize as categorias que agrupam os pratos no cardápio.', 'Só uma categoria vazia pode ser excluída. Mude os pratos de categoria antes de excluir.'],
  'producao/receitas': ['Escolha o prato e confira o rendimento e a unidade da receita.', 'Associe os insumos com suas quantidades. O custo por porção depende dessas informações.'],
  'producao/insumos': ['Cadastre ingredientes, intermediários e embalagens com a unidade que você usa na produção.', 'Confira saldo, custo e unidades antes de salvar. Uma embalagem é consumida por unidade.'],
  'producao/planejamento': ['Escolha o período e revise as quantidades sugeridas antes de preparar a produção.', 'Use as faltas de insumos e a lista de compras para organizar o trabalho. Revise o resultado na Produção do dia.'],
  'producao/perdas': ['Registre o item, a quantidade e o motivo da perda.', 'Confira o período do resumo e os itens vencidos sugeridos antes de lançar.'],
  'producao/calculadora': ['Escolha uma receita, informe a quantidade e a unidade desejadas e calcule.', 'Confira as faltas e o custo estimado. A simulação não altera o estoque; custos ausentes precisam ser conferidos antes da produção.'],
  'financeiro/relatorios': ['Escolha as datas De e Até para consultar o período.', 'Confira o fluxo por vencimento e pagamentos. Use Ampliar relatório para ler a tabela ou Exportar CSV para trabalhar com os dados.'],
  'configuracoes/impressao': ['Em Papel padrão neste aparelho, escolha o formato de cada documento. A preferência é guardada neste navegador e pode ser trocada ao imprimir.', 'O logo da loja é compartilhado nos próximos impressos. Na térmica, confira a legibilidade antes de ativar o logo.'],
  'entregas/agenda': ['Escolha Primeiro dia para consultar sete dias. Use Semana anterior e Próximos 7 dias para mudar o período.', 'Confira as reservas ativas, as vagas ocupadas e os avisos Dia fechado, Janela inativa ou Bloqueada antes de combinar a entrega.', 'Se a consulta falhar, use Atualizar agenda. Uma falha de carregamento não confirma que a janela está vazia.'],
  'entregas/janelas': ['Confira o dia da semana, o horário e a capacidade antes de salvar uma janela.', 'A pausa interrompe a oferta da janela. Confira as vagas antes de combiná-la com o cliente.'],
}

export function contextoDaAjuda(rota, fonteApi) {
  const modulo = moduloPorId(moduloDaRota(rota ?? {}))
  const telas = telasDoMenu(modulo, { fonteApi })
  const tela = telas.find(t => t.id === rota?.tela) ?? telas[0]
  const producaoNaoLigada = fonteApi && modulo?.id === 'cozinha' && tela?.id === 'producao'
  return {
    titulo: tela ? `${modulo.nome} · ${tela.rotulo}` : modulo?.nome ?? 'Módulos',
    passos: producaoNaoLigada ? ['Esta tela ainda não está disponível.', 'Para registrar o preparo, abra Produção e escolha Produção do dia.'] : TELAS[`${modulo?.id}/${tela?.id}`] ?? GUIAS[modulo?.id] ?? [
      'Busque um módulo ou uma função pelo nome.',
      'Os módulos disponíveis seguem seu perfil. Para voltar ao atendimento, abra Atendimento e escolha Balcão.',
    ],
    atalho: producaoNaoLigada ? { modulo: 'producao', tela: 'producao', rotulo: 'Ir para Produção do dia' } : null,
  }
}

// Allowlist: a origem, a hash bruta e os dados da sessão nunca entram neste texto.
export function diagnosticoDoConsole({ rota, fonteApi, versao, onLine }) {
  return [
    'Diagnóstico do Console EasyStok',
    `Tela: ${contextoDaAjuda(rota, fonteApi).titulo}`,
    `Versão: ${/^[a-f0-9]{7,40}$/i.test(versao ?? '') ? versao : 'não informada'}`,
    `Dados: ${fonteApi ? 'API do EasyStok' : 'demonstração'}`,
    `Rede do aparelho: ${onLine === false ? 'offline indicado pelo navegador' : onLine === true ? 'disponível; serviço ainda não verificado' : 'não informada'}`,
  ].join('\n')
}

export async function copiarDiagnostico(texto, areaTransferencia) {
  try {
    if (!areaTransferencia?.writeText) return false
    await areaTransferencia.writeText(texto)
    return true
  } catch { return false }
}
