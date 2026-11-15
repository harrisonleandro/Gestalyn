# Fase 09: Interface web completa

- **Estado:** Planned. Aguarda a aprovação das decisões D1 a D8.
- **Referência:** [Especificação mestre](ESPECIFICACAO-MESTRE.md), seção 9 (Fase 09) e seção 8 (experiência de usuário e interface)
- **Branch:** `feature/fase-09-web` (a partir de `develop` `67144d1`, com as Fases 01 a 08 integradas)
- **Pré-requisito para implementar:** decisões aprovadas

## Objetivo

Construir a experiência completa de uso na Web (Blazor), conectada à API das Fases 03 a 08:
- organizações, projetos, equipes, tarefas (lista e quadro), workflow, fases e marcos;
- atividades, documentos, decisões e dashboards.

As telas devem refletir as permissões do usuário, sempre validadas também no backend, e devem ser responsivas, acessíveis e consistentes. Não podem apresentar dados fictícios como reais.

## Situação atual

- **Páginas existentes:** cadastro, entrada, confirmação de e-mail, recuperação de senha, perfil, aceitação de convite e status do sistema.
- **Arquitetura:** a Web é BFF (ADR-006).
  - Os tokens da API ficam no cookie HttpOnly e são renovados no servidor (`TokenRefreshCookieEvents`).
  - As páginas são **SSR estáticas**, com formulários e antiforgery. As chamadas autenticadas usam o token do `HttpContext`.
- **Visual:** Bootstrap 5.3 (incluído em `wwwroot/lib`) com tema escuro (`data-bs-theme="dark"`), mais algumas classes próprias em `app.css`.
- **Pendência registrada:** o ADR-006 já indicava que componentes interativos que chamem a API precisariam de um provedor de token por circuito, a ser decidido nesta fase (D2).

## Escopo

1. **Design system** (D1): tokens visuais, componentes reutilizáveis e padrões de estado (carregando, vazio, erro, sucesso).
2. **Layout e navegação:** menu lateral responsivo, organização ativa, trilha de navegação e menu do usuário.
3. **Autenticação visual:** as telas de conta existentes, revisadas com o design system.
4. **Organizações** (D3): seleção e criação de organização, e configurações (nome, membros, papéis e convites).
5. **Dashboard** da organização e do projeto, com o progresso e as contagens da Fase 08.
6. **Projetos:** lista com filtros, criação, detalhes, edição, arquivamento, etiquetas, repositórios e equipe.
7. **Tarefas:**
   - lista com filtros, ordenação e paginação;
   - **quadro por estado** (D5);
   - detalhes com responsáveis, etiquetas, dependências, comentários, anexos, transições disponíveis e histórico.
8. **Workflow:** configuração de estados, categorias, ordem, transições e aprovação, com aviso quando um estado não alcança um estado final.
9. **Fases e marcos:** criação, edição, reordenação, exclusão e progresso.
10. **Atividades:** lista com filtros e registro manual.
11. **Documentos e decisões:**
    - documentos com edição pela versão esperada, histórico de versões, arquivamento e restauração;
    - decisões com proposta, aceite, rejeição e substituição;
    - Markdown renderizado com segurança (D6).
12. **Permissões refletidas na interface** (D4): ações sem permissão não aparecem ou ficam desabilitadas com explicação, e a resposta 403 da API é tratada.
13. **Estados de carregamento e erro**, confirmação de ações destrutivas e retorno de cada operação.
14. **Responsividade e acessibilidade** (D7).
15. **Testes e evidências** (D8).

## Fora de escopo

- **Interface de agentes, credenciais e integrações** (F09-018 e as telas "Agentes", "Credenciais" e "Integrações" da seção 8.1): dependem da API de agentes (Fase 10) e das integrações (Fase 12). Conflito de ordem na especificação mestre, tratado em D7.
- **Tempo real com SignalR** (F09-021): a seção 8.2 pede SignalR só com necessidade real. Tratado em D2.
- **Arrastar e soltar no quadro:** o quadro move tarefas pelas transições disponíveis, com botões (D5).
- **Tema claro e troca de idioma:** a direção é o tema escuro, e a interface é em pt-BR.
- **Mudanças de regra de negócio na API:** a única adição prevista é a rota de leitura da D4.

## Decisões (D1 a D8, para aprovação)

| ID | Proposta | Alternativas |
|---|---|---|
| **D1** Design system | **Bootstrap 5.3, que já está no projeto, com tokens próprios** (cores, espaçamentos, raios, tipografia) em `app.css`, no tema escuro. Por cima dele, **componentes Razor próprios** e reutilizáveis: <br>- cabeçalho de página e trilha; <br>- tabela com paginação; <br>- estado vazio, carregando e erro; <br>- confirmação de ação destrutiva; <br>- mensagem de retorno; <br>- selo de estado com a cor da categoria; <br>- barra de progresso que mostra as contagens; <br>- campos de formulário com erro de validação. <br>Ícones em **SVG embutido**, sem pacote novo. **Nenhuma dependência nova de interface.** | (a) MudBlazor ou Fluent UI: componentes prontos, mas um pacote grande, com estilo próprio difícil de alinhar à identidade e acoplado à interatividade. (b) Tailwind: exige etapa de build com Node. |
| **D2** Modo de renderização | **SSR estático com navegação e formulários aprimorados** (`enhanced navigation`) e **streaming rendering** para os estados de carregamento, mantendo o modelo da Fase 03. <br>- Cada requisição tem o `HttpContext`, e o token sai do cookie, renovado pelos eventos existentes. <br>- As ações são formulários com antiforgery; a página volta atualizada, sem recarregar tudo. <br>- **Interatividade no navegador só onde não chama a API em nome do usuário**: abrir e fechar o diálogo de confirmação, o menu em telas pequenas e a pré-visualização local. São pequenos módulos JS ou recursos nativos de HTML (`<dialog>`, `<details>`). <br>- **Sem SignalR nesta fase** (F09-021): nenhuma tela tem necessidade comprovada de atualização em tempo real; recarregar e navegar trazem os dados atuais. Fica registrado para reavaliação. | (a) Interactive Server nas telas: exigiria guardar e renovar os tokens dentro do circuito. A rotação de refresh token (ADR-012) não atualiza o cookie a partir de um circuito, e a mesma sessão renovaria em dois lugares, o que a detecção de reuso trataria como ataque. (b) WebAssembly: os tokens iriam para o navegador, o que o ADR-006 proíbe. |
| **D3** Organização ativa na Web | **A organização fica na URL**, como na API: `/o/{organizationId}/...`. <br>- Depois de entrar, quem participa de uma organização vai direto para ela; quem participa de várias escolhe em `/organizacoes`. <br>- A última organização usada fica num cookie não sensível (só o ID), para o redirecionamento depois de entrar. <br>- A troca de organização fica no menu. <br>- Um ID de organização sem associação responde 404, como na API. | Organização só no cookie: URLs que não dá para compartilhar, e duas abas em organizações diferentes se atrapalhariam. |
| **D4** Permissões na interface | A interface decide o que mostrar pelas **permissões que a API devolve**: <br>- `permissions` nos detalhes do projeto e da tarefa; <br>- `availableTransitions` nos detalhes da tarefa; <br>- as permissões da organização, por uma **rota nova de leitura, `GET /organizations/{id}/me`**, que devolve o papel e as permissões do usuário naquela organização. Hoje a Web teria de cruzar a lista de organizações com a lista de papéis, que exige `members.read`. <br>Ações sem permissão não aparecem; quando o contexto ajuda, aparecem desabilitadas com a explicação. **Toda ação continua validada no backend**: a interface trata 403, 404 e 409 com mensagens claras. A rota nova não muda nenhuma regra e entra no ADR-015, com testes de integração. | Calcular as permissões na Web a partir dos papéis: duplicaria a regra de acesso fora do backend, o que o AGENTS.md proíbe. |
| **D5** Quadro de tarefas | **Uma coluna por estado do workflow**, na ordem configurada, com a contagem e o selo da categoria. <br>- Cada cartão mostra número, título, responsáveis, prazo (destacado se vencido), prioridade e etiquetas. <br>- **Mover uma tarefa é um botão "Mover para…"** com as transições disponíveis para o usuário, que já vêm filtradas por permissão e aprovação (Fase 07). As que exigem aprovação aparecem marcadas. <br>- Os filtros são os mesmos da lista. <br>- Colunas com muitas tarefas mostram as 50 primeiras e um link para a lista filtrada pelo estado. | (a) Arrastar e soltar: exige interatividade com a API (D2), e o arraste também precisaria de uma alternativa por teclado. Pode entrar depois, como melhoria. (b) Quadro por categoria: esconderia os estados que o projeto configurou. |
| **D6** Markdown | **Markdig** (licença BSD-2, compatível com .NET 10) para renderizar descrições, comentários, documentos e decisões: <br>- com o **HTML cru desabilitado** (`DisableHtml`); <br>- com links e imagens restritos a `http`, `https` e `mailto`; <br>- links externos com `rel="noopener noreferrer nofollow"`. <br>Uma **Content-Security-Policy** na Web (sem scripts inline nem de outras origens) reforça a proteção. A edição usa uma área de texto com pré-visualização. É a única **dependência nova**, justificada no ADR-015. | (a) Mostrar o Markdown como texto puro: seguro, mas perde a formatação que é o motivo do formato. (b) Markdig com HTML liberado e um sanitizador (HtmlSanitizer): duas dependências e mais superfície. (c) Renderizar no navegador com uma biblioteca JS: o conteúdo do usuário viraria HTML fora do servidor. |
| **D7** Escopo das telas e o conflito com a Fase 10 | A Fase 09 lista "Implementar interface de agentes" (F09-018), e a seção 8.1 lista as telas "Agentes", "Credenciais" e "Integrações". A **API de agentes só existe na Fase 10**, e as integrações, na Fase 12. Construir as telas agora exigiria dados fictícios ou telas que não funcionam, o que a especificação proíbe. **Proposta:** <br>- F09-018 e essas três telas passam para as fases em que a API existir (10 e 12); <br>- o menu não mostra entradas para elas; <br>- o desvio fica registrado no ADR-015 e no `PROJECT_STATUS.md`. <br>**Configurações** (F09-017) nesta fase: a organização (nome, membros, papéis, convites), o projeto (dados, equipe, etiquetas, repositórios, workflow) e o perfil. <br>**Acessibilidade e responsividade:** <br>- navegação por teclado; <br>- foco visível; <br>- rótulos em todos os campos; <br>- contraste AA no tema escuro; <br>- cores nunca como única informação; <br>- leiaute de 360 px a 1920 px, com o menu recolhível e tabelas que viram cartões em telas pequenas. | Uma tela de agentes "em breve": telas que aparentam existir sem implementação contrariam o AGENTS.md. |
| **D8** Testes e evidências | **Testes de componentes com bUnit** (licença MIT, dependência só de teste) num projeto novo, `tests/ProjectManager.WebTests`, que referencia a Web e mais nenhum projeto da solução. Cobrem: <br>- os estados de carregamento, vazio e erro; <br>- as ações escondidas sem permissão; <br>- o quadro (colunas e transições); <br>- a renderização segura do Markdown (HTML, `javascript:` e imagens de outros esquemas); <br>- o tratamento de 403, 404 e 409. <br>Os clientes da API são substituídos nesses testes por implementações de teste das interfaces da Web. <br>**Execução real no navegador**, com a API e a Web rodando sobre o banco local: <br>- os fluxos principais, com dois usuários de papéis diferentes; <br>- capturas de tela em largura de computador (1440 px) e de celular (375 px); <br>- a verificação de que a Web nunca recebe tokens no navegador (cookies e respostas). <br>A rota `GET /organizations/{id}/me` ganha testes de integração na API. | (a) Playwright (testes de ponta a ponta): mais fiéis, mas exigem navegadores no CI. A especificação os coloca na Fase 13. (b) Só verificação manual: sem regressão automatizada da interface. |

### Decisões de baixo risco (serão registradas no ADR-015)

- **Clientes da API na Web:**
  - um cliente tipado por área (organizações, projetos, planejamento, tarefas, workflow, atividades, documentos, dashboards), todos sobre o `ApiRequests` existente;
  - DTOs próprios da Web: ela não referencia Domain nem Application;
  - o cabeçalho `X-Gestalyn-Organization` é enviado nas rotas por ID.
- **Datas:** mostradas em pt-BR. Datas com hora usam o fuso configurado na Web (`Display:TimeZone`, padrão `America/Sao_Paulo`). Prazos (`DateOnly`) não têm fuso.
- **Rotas da Web em pt-BR:**
  - `/o/{org}/projetos/{id}`;
  - `/o/{org}/projetos/{id}/tarefas`, `/quadro`, `/fases`, `/atividades`, `/documentos`, `/decisoes` e `/configuracoes`;
  - `/o/{org}/tarefas/{id}`.
- **Ações destrutivas** (arquivar, excluir estado, fase, marco, comentário ou anexo, remover da equipe ou da organização, rejeitar decisão): exigem confirmação num diálogo que diz o efeito e se dá para desfazer.
- **Retorno das operações:** uma mensagem de sucesso aparece depois do redirecionamento (padrão post-redirect-get com mensagem de uma vez), e os erros de validação aparecem ao lado dos campos.
- **Anexos:** o envio é por formulário `multipart` com antiforgery, sem ultrapassar o limite da API (10 MB). O download passa pela Web, que repassa o arquivo da API como anexo, sem expor o token.
- **Página inicial sem organização:** a pessoa vê a criação de organização e os convites pendentes, sem números fictícios.
- **A página "Status do sistema"** continua acessível no rodapé.

## Contratos previstos

| Item | Detalhe |
|---|---|
| `GET /api/v1/organizations/{organizationId}/me` (API, nova) | Membro da organização (`organization.read`). Responde `{ organizationId, name, roleId, roleName, isOwner, permissions }` |
| Rotas da Web | As da seção anterior, todas com `[Authorize]` exceto as de conta |

## Dependências

| Item | Estado |
|---|---|
| Fases 03 a 08 (API completa das telas) | Completed, integradas em `develop` |
| Markdig (D6) | Pacote novo, licença BSD-2, compatível com .NET 10; versão fixada em `Directory.Packages.props` |
| bUnit (D8) | Pacote novo só de teste, licença MIT, compatível com .NET 10 |
| Docker (PostgreSQL e Mailpit) | Instalado; precisa estar em execução para os testes de integração e a execução real |

## Tarefas

| ID | Tarefa | Subtarefas |
|---|---|---|
| F09-001 | Definir design system | Tokens, componentes reutilizáveis, padrões de estado (D1) |
| F09-002 | Criar layout principal | Menu lateral responsivo, cabeçalho, rodapé |
| F09-003 | Criar navegação | Trilha, organização ativa na URL, menu do usuário (D3) |
| F09-004 | Implementar autenticação visual | Telas de conta no design system |
| F09-005 | Implementar seleção de organização | `/organizacoes`, criação, cookie da última organização, rota `GET /organizations/{id}/me` (D3 e D4) |
| F09-006 | Implementar dashboard | Organização e projeto, com progresso e contagens |
| F09-007 | Implementar lista de projetos | Filtros, criação, arquivados |
| F09-008 | Implementar detalhes de projeto | Dados, etiquetas, repositórios, arquivamento |
| F09-009 | Implementar gestão de membros | Equipe do projeto; membros, papéis e convites da organização |
| F09-010 | Implementar quadro de tarefas | Colunas por estado e "Mover para…" (D5) |
| F09-011 | Implementar lista de tarefas | Filtros, ordenação, paginação, criação |
| F09-012 | Implementar detalhes de tarefa | Edição, responsáveis, dependências, comentários, anexos, transições, histórico |
| F09-013 | Implementar configuração de workflow | Estados, transições, aprovação, aviso de estado sem caminho até um final |
| F09-014 | Implementar fases e marcos | Criação, edição, reordenação, exclusão, progresso |
| F09-015 | Implementar atividades | Lista, filtros, registro manual |
| F09-016 | Implementar documentos e decisões | Versões, conflito de versão, estados das decisões, Markdown seguro (D6) |
| F09-017 | Implementar configurações | Organização, projeto, perfil (D7) |
| F09-018 | Implementar interface de agentes | **Proposta: transferir para a Fase 10** (D7) |
| F09-019 | Implementar estados de carregamento e erro | Streaming rendering, estados vazios, tratamento de 403, 404 e 409 |
| F09-020 | Implementar responsividade e acessibilidade | De 360 px a 1920 px, teclado, foco, rótulos, contraste (D7) |
| F09-021 | Implementar atualizações em tempo real quando justificadas | **Proposta: não implementar nesta fase**, sem necessidade comprovada (D2) |

## Arquivos previstos

```
src/ProjectManager.Web/Api/                     Clientes por área e DTOs da Web
src/ProjectManager.Web/Components/Layout/       Layout, menu, trilha, seletor de organização
src/ProjectManager.Web/Components/Shared/       Componentes do design system
src/ProjectManager.Web/Components/Pages/        Organizações, Dashboard, Projetos, Tarefas, Quadro, Workflow,
                                                Planejamento, Atividades, Documentos, Decisões, Configurações
src/ProjectManager.Web/Markdown/                Renderização segura (Markdig)
src/ProjectManager.Web/wwwroot/app.css          Tokens e estilos do design system
src/ProjectManager.Web/wwwroot/js/              Módulos pequenos (diálogo de confirmação, menu)
src/ProjectManager.API/Controllers/OrganizationsController.cs  GET /organizations/{id}/me
tests/ProjectManager.WebTests/                  Testes de componentes (bUnit)
tests/ProjectManager.IntegrationTests/Organizations/  Testes da rota nova
docs/decisions/ADR-015-INTERFACE-WEB.md
docs/web/TELAS.md                               Mapa das telas, rotas e permissões
```

## Critérios de aceitação

| Critério (especificação mestre) | Como será verificado |
|---|---|
| Todas as telas principais funcionais | Execução real de cada tela no navegador, com dados criados pela própria interface; mapa das telas em `docs/web/TELAS.md` |
| Interface conectada à API | Toda tela lê e grava pela API; nenhum dado é gravado pela Web por outro caminho |
| Nenhum dado fictício apresentado como real | Estados vazios em vez de exemplos; nenhuma tela para funcionalidades sem API (D7); revisão das telas |
| Erros tratados | Testes de componentes para 403, 404, 409, validação e API indisponível; execução real com conflito de versão e transição recusada |
| Permissões refletidas na interface e validadas no backend | Testes de componentes que escondem ações sem permissão; execução real com Observador e Colaborador; tentativas diretas recebendo 403 da API |
| Responsividade validada | Capturas de tela em 375 px e 1440 px de cada tela principal; menu recolhível e tabelas em cartões |
| Navegação consistente | Mesmo layout, trilha e padrões de ação em todas as telas; navegação por teclado verificada |

## Estratégia de testes

- **Componentes (bUnit):** estados, permissões, quadro, Markdown seguro, formulários e tratamento de erros.
- **Integração (API):** `GET /organizations/{id}/me`, com papéis diferentes, não membro (404) e isolamento.
- **Mutação:** mostrar uma ação sem a permissão correspondente e liberar HTML cru no Markdown devem fazer os testes falharem.
- **Execução real:**
  - API e Web rodando sobre o banco local, com Gerente, Colaborador e Observador;
  - fluxo completo: organização, projeto, equipe, workflow, tarefas no quadro até a conclusão com aprovação, documento com conflito de versão, decisão aceita e dashboards;
  - capturas em 1440 px e 375 px;
  - inspeção dos cookies e das respostas para confirmar que nenhum token chega ao navegador.

## Riscos

| Risco | Mitigação |
|---|---|
| Volume da fase (21 tarefas) | Entrega por partes, um commit por área: design system e navegação; organizações e projetos; tarefas, quadro e workflow; planejamento, atividades, documentos e dashboards; acessibilidade e revisão |
| SSR com formulários parecer menos fluido que uma interface interativa | Navegação e formulários aprimorados (sem recarregar a página inteira), streaming rendering e mensagens de retorno; interatividade com a API pode ser reavaliada com um provedor de token por circuito |
| XSS pelo Markdown | HTML cru desabilitado, esquemas restritos, CSP; testes com conteúdo malicioso |
| Duplicar regras de negócio na Web | A Web só decide o que mostrar a partir das permissões e transições que a API devolve; nenhuma regra é recalculada |
| Datas exibidas em fuso errado | Fuso configurável, padrão `America/Sao_Paulo`; prazos sem fuso |
| Dependências novas | Só Markdig (produção) e bUnit (teste), com versões fixas no Central Package Management e licenças verificadas |

## Definição de conclusão

- Todos os critérios verificados com evidência, local e no CI.
- Mutações das permissões na interface e do Markdown detectadas.
- ADR-015 registrado, inclusive o desvio de F09-018 e F09-021, se aprovado.
- Documentação atualizada: `docs/web/TELAS.md`, README, AGENTS.md, ARCHITECTURE, `PROJECT_STATUS.md` e relatório da Fase 09.
- Alterações integradas por PR, com o check "Build e testes" aprovado.
