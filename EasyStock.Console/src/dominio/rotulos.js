// #2944 (E1-11): o glossário único do que a tela diz. Rótulo é exibição: trocar aqui não muda nenhuma decisão.
// Quem decide usa código (`CODIGOS`) ou a frase que a API grava (`FRASES_DA_API`), nunca estes textos.

export const ROTULOS = {
  PEDIDO: 'Pedido',
  PRAZO_24H: 'prazo de 24 h',
  MENSAGEM_PADRAO: 'mensagem padrão do WhatsApp',
  SEM_NOME: 'Contato sem nome',
  CHIP_AJUDA_NO_PEDIDO: 'Ajuda no pedido',
  ASSISTENTE_RESPONDENDO: 'Assistente respondendo',
  CONVERSA_ASSUMIDA_POR_VOCE: 'Conversa assumida por você',
}

// Valores que a API e o provedor gravam. A tela só os reconhece; quem os escreve é o EasyStok.
export const CODIGOS = {
  FORA_DA_JANELA: 'fora_da_janela_24h',
  // Nome que o EasyStok grava no lead sem nome de perfil (#2276). É valor de dado, não rótulo.
  NOME_PADRAO_DO_LEAD: 'cliente whatsapp',
}

// Frases do backend que o Console reconhece (ComandaClienteUseCase.cs). A API não muda nesta PR.
export const FRASES_DA_API = {
  AJUDA_PELA_COMANDA: /^cliente pediu ajuda pela comanda/i,
}
