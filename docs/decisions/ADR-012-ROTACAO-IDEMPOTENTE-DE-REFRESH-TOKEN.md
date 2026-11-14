# ADR-012: Rotação idempotente de refresh token com detecção de divergência

- **Status:** Aceito
- **Data:** 2026-11-14
- **Origem:** auditoria técnica de 2026-11-12, achado P1. O plano de correção foi aprovado pelo responsável.
- **Substitui:** a regra de "janela de 30 s com 401 sem revogar" do [ADR-006](ADR-006-AUTENTICACAO-E-SESSAO.md).

## Contexto

O ADR-006 definiu refresh tokens de uso único, com rotação, e revogação da sessão quando um token já trocado é reapresentado. Para não revogar sessões em requisições simultâneas legítimas, criou uma exceção: dentro de 30 s, reapresentar o token respondia só 401, sem revogar.

A auditoria confirmou que essa exceção favorece o invasor:

1. O invasor usa o refresh token roubado primeiro e recebe um par novo.
2. A vítima apresenta o mesmo token dentro dos 30 s e recebe 401, sem revogação.
3. A Web trata o 401 como sessão encerrada e desconecta a vítima, sem nenhum registro.
4. O invasor continua renovando a sessão, por até 30 dias.

Interceptar a própria requisição de refresh (no cliente ou num proxy) torna trivial chegar primeiro.

A exceção também não protegia as várias instâncias da Web, ao contrário do que dizia o ADR-006: a instância que perdia a corrida recebia 401 e desconectava o usuário.

O servidor não consegue distinguir, dentro de uma janela, uma requisição simultânea legítima de um ataque. A resposta precisa ser segura nos dois casos.

## Decisão

1. **Cadeia de rotação:** cada refresh token guarda o sucessor emitido na troca (`replaced_by_token_id`, único) e quantas repetições aceitou (`replay_count`).
2. **Sucessor determinístico:** o sucessor é `HMAC-SHA256(K, token apresentado)`, com `K` derivada da chave de assinatura JWT por HKDF (`info = "gestalyn/refresh-token-successor/v1"`), como em `RefreshTokenDerivation`.
   - Qualquer instância da API recalcula o mesmo sucessor a partir do token apresentado.
   - O sucessor nunca é gravado em texto, e não há segredo nem pacote novo.
   - Sem `K`, ninguém calcula o sucessor, mesmo de posse do token.
   - O login continua emitindo um token aleatório de 256 bits.
3. **Regra única de decisão** (`RefreshDecision`):

   | Situação | Ação |
   |---|---|
   | Sessão revogada ou expirada | 401, sem alterar nada |
   | Token ainda não usado | Rotação: consome o token e emite o sucessor |
   | Token já trocado, dentro da janela, com o sucessor ainda não usado e abaixo do limite de repetições | **Repetição:** devolve **o mesmo sucessor** e um access token novo |
   | Qualquer outro reuso: fora da janela, sucessor já usado, limite excedido ou sucessor desconhecido | **Revoga a sessão** (`RefreshTokenReuse`) e responde 401 |

   Não existe mais "401 sem revogar" para um token já trocado.
4. **Janela e limite:**
   - `Authentication:Jwt:RefreshReplayWindow` vale 10 s por padrão, o mesmo timeout das chamadas da Web à API. Fica entre 0 e 30 s, e zero é o modo rígido, em que todo reuso revoga.
   - `MaxRefreshReplays` vale 5 por padrão (de 0 a 10).
5. **Concorrência:**
   - **Rotações simultâneas:** a primeira grava (o `xmin` do token e a unicidade do hash do sucessor garantem isso). As demais releem o estado e viram repetições.
   - **Repetições:** incrementam o contador com um `UPDATE` atômico e condicional ao limite, sem conflito entre si.
   - Depois de 3 tentativas seguidas com conflito, a requisição recebe 401 e o caso é registrado em log.
   - O estado fica só no PostgreSQL, então tudo funciona com várias instâncias da API e da Web, sem Redis nem trava distribuída.
6. **Logs:**
   - repetição: nível Information, com sessão, usuário e número da repetição;
   - reuso: nível Warning, quando a sessão é revogada;
   - nunca com tokens.

### Efeito no cenário adversarial

- **O invasor usa o token primeiro e não avança a cadeia:** a vítima, dentro da janela, recebe o mesmo sucessor e continua conectada. Os dois ficam na mesma cadeia, e na próxima rotação quem chegar depois da janela revoga a sessão: o invasor perde o acesso.
- **O invasor avança a cadeia:** a vítima apresenta o token antigo, o sucessor já foi usado, e a sessão é revogada na hora, mesmo dentro da janela.
- **O invasor só escapa** acompanhando cada refresh da vítima dentro da janela, o que exige interceptação contínua do cliente (risco residual, abaixo).

## Alternativas consideradas

| Alternativa | Motivo da rejeição |
|---|---|
| Manter a janela com 401 sem revogar | Desconecta a vítima e mantém o invasor, sem registro |
| Modo rígido como padrão (todo reuso revoga) | Revogaria sessões legítimas em corridas entre instâncias da Web ou processos paralelos da CLI. Continua disponível com janela 0, para quem tiver afinidade de sessão |
| Trava distribuída (Redis) | Infraestrutura nova sem necessidade: o PostgreSQL já serializa |
| Guardar o sucessor criptografado (Data Protection) | Exige um repositório de chaves compartilhado entre instâncias e um pacote novo |
| Tokens vinculados ao cliente (DPoP) | É a proteção mais forte, mas muda o protocolo de todos os clientes. Fica como evolução |

## Consequências

- **Sem o sucessor registrado:** tokens trocados antes da migration `AddRefreshTokenSuccessor` não têm sucessor e são tratados como reuso. Quem estiver no meio de uma renovação no momento do deploy pode ser desconectado.
- **Troca da chave JWT:** invalida as repetições em andamento (no máximo 10 s); depois disso, o reuso é tratado normalmente.
- **Clientes que recebem o mesmo par duas vezes** continuam funcionando. O `TokenRefreshCoordinator` da Web continua evitando chamadas repetidas dentro de cada instância.
- **Risco residual:** a interceptação contínua do cliente só seria resolvida com tokens vinculados ao cliente (DPoP). As sessões continuam com vida absoluta de 30 dias e sem expiração por inatividade; isso fica para decisão futura.
