# Contingência sem a VPS (#2132)

Para continuar o desenvolvimento quando a VPS está fora: laboratório local com uma cópia do banco de produção e um túnel público para os webhooks. Custo zero, sem conta nova, sem IP fixo.

| Passo | Comando |
|---|---|
| 1. Cópia do banco (com a VPS no ar) | `ssh hostinger 'docker exec shared-postgres pg_dump -U postgres -Fc easystock' > easystock.dump` |
| 2. Laboratório com o dump | `powershell -File scripts/homologacao/laboratorio.ps1 -Dump <easystock.dump> -Pasta <pasta nova>` |
| 3. Túnel para a API | `powershell -File scripts/contingencia/tunel.ps1` |
| 4. Fechar | `tunel.ps1 -Parar` e `laboratorio.ps1 -Parar -Pasta <a mesma>` |

Guarde o dump e o `.env` fora do repositório: têm dados de clientes e segredos. Com `-Dump` a chave JWT é sorteada a cada subida, porque pelo túnel a API fica na internet; feche o túnel quando não estiver testando webhook.

## O que funciona e o que não

| Item | Situação |
|---|---|
| Console, pedidos, estoque, caixa | Com os dados do dump; login com os usuários de produção |
| Mercado Pago | Falso por padrão (`fake_mp.py`). Para o real, troque `MercadoPago__*` e aponte o `NotificationUrl` para a URL do túnel |
| WhatsApp | Só chega se o webhook do app na Meta apontar para `<túnel>/api/webhooks/whatsapp` e a API tiver o `AppSecret` e o `VerifyToken` do app |
| Campos cifrados com a KEK de produção | Não abrem sem `EZ_CRYPTO_KEK`; credenciais de canal ficam ilegíveis |
| URL do túnel | Muda a cada abertura (`*.trycloudflare.com`). Para URL fixa: Cloudflare Tunnel nomeado, que exige conta e DNS na Cloudflare |

Domínio próprio apontado para casa não serve: a maioria das operadoras usa CGNAT e bloqueia as portas 80 e 443.
