# Vigia de cache das sessões ativas (20/11/2026 13:10)

## Memória de contexto (sessões com atividade na última hora)

| Sessão | Contexto | Estado 13:10 | PR |
|---|---|---|---|
| local_ec3a4d1f | Ferramentas do Easy Stok plugin | rodando | #156 merged |
| local_96fdd476 | Pesquisa MCPs, ads e captação de leads | rodando | #116 merged |
| local_2e483f36 | Evolução do atendimento (origem anúncio S5/S6, #2928) | rodando | #2898 merged |
| local_6af51769 | Hosts, políticas e economia de tokens | parada | N/A |
| local_3e86ca31 | Auditoria UX Captura Anamnesis | parada | #2945 merged |
| local_3a00b894 | Domínio da API do agente LLM (tabelas IA #2957) | parada | #2949 merged |
| local_1251eadf | Repositório de código na VPS com backup | parada | N/A |
| local_9ec5a7ec | Recuperar design system FMA software | parada | #15 merged |

Fora da janela (cache já expirou, >1 h): Headroom compressor aviso, Captura Anamnesis 19/11, Corrigir 422 gateway foto (PR #15 aberta).

## Loop

Varredura a cada 10 min; cada sessão PARADA recebe 1 ping quando completa 45 a 55 min sem atividade (efeito: ping por sessão a cada ~50 min, renovando o TTL de 1 h). Sessões rodando já mantêm o cache quente sozinhas e não recebem ping.

## Economia (preços relativos ao input base, prefixo P tokens)

| Operação | Custo |
|---|---|
| Leitura de cache | 0,1 P |
| Escrita cache TTL 5 min | 1,25 P |
| Escrita cache TTL 1 h | 2 P |

| Cenário (por hora ociosa) | TTL 5 min | TTL 1 h (atual) |
|---|---|---|
| Sem ping, volta depois de 30 min | 1,25 P | 0,1 P |
| Sem ping, volta depois de 2 h | 1,25 P | 2 P |
| Ping a cada 4 min (15/h) | 1,5 P + saída | 1,5 P + saída |

Exemplo P = 150 mil tokens: ping custa 15 mil por vez, 225 mil por hora por sessão; reescrever custa 300 mil (1 h) ou 187 mil (5 min).

**Veredito:** com TTL 1 h (configuração atual) o ping de 4 min só compensa se a sessão ficar ociosa entre 45 min e o retorno e for voltar a ser usada; abaixo de 45 min o próprio TTL segura o cache de graça. Em TTL 5 min o ping empata (1,5 P contra 1,25 P) e perde. Recomendação: intervalo de 50 a 55 min, não 4 min, e só para sessões que vão voltar.
