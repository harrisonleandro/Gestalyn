# Relatório final: Fase 09, Interface web completa

- **Data:** 2026-11-15
- **Ambiente:** Windows 11 Pro · .NET SDK 10.0.404 · Docker 29.8.2 · PostgreSQL 18 · Mailpit v1.29 · navegador do Claude Desktop
- **Branch:** `feature/fase-09-web` (a partir de `develop` `67144d1`, com as Fases 01 a 08 integradas)
- **Estado:** **In Review.** Critérios verificados localmente; faltam o push, o CI no GitHub e a integração em `develop` por PR, que dependem de autorização do responsável.
- **Formato:** seção 17 da [especificação mestre](../specifications/ESPECIFICACAO-MESTRE.md)

## 1. Nome e número da fase

Fase 09: Interface web completa. A especificação da fase está em [FASE-09-WEB.md](../specifications/FASE-09-WEB.md), as decisões no [ADR-015](../decisions/ADR-015-INTERFACE-WEB.md) e o mapa das telas em [docs/web/TELAS.md](../web/TELAS.md).

## 2. Objetivo

Construir a experiência completa de uso na Web, conectada à API das Fases 03 a 08:
- com as permissões refletidas na interface e validadas no backend;
- responsiva e acessível;
- sem dados fictícios.

## 3. Resumo das implementações

- **Design system (D1):**
  - tokens próprios sobre o Bootstrap 5.3 no tema escuro;
  - componentes reutilizáveis: cabeçalho e trilha, estados, estado vazio, ação com confirmação, selo de estado, progresso com contagens, paginação, Markdown, lista de atividades, molduras de organização e projeto;
  - ícones em SVG embutido.
- **Renderização (D2):**
  - SSR com navegação e formulários aprimorados, e streaming rendering nas páginas de leitura;
  - post-redirect-get com aviso por chave conhecida;
  - diálogo nativo de confirmação;
  - sem SignalR.
- **Organização ativa (D3):** na URL `/o/{id}`, confirmada por `GET /organizations/{id}/me`. A última organização fica num cookie com só o ID, apagado ao sair.
- **Permissões (D4):** rota nova `GET /api/v1/organizations/{id}/me`. A tela mostra as ações pelas permissões e transições que a API devolve.
- **Telas (D5 e D7):**
  - organizações e configurações da organização (membros, convites, papéis);
  - visão geral da organização;
  - projetos: lista, criação, visão geral e configurações (dados, equipe, repositórios, arquivamento);
  - tarefas: lista, quadro por estado e detalhes (transições, edição, responsáveis, dependências, comentários, anexos, histórico);
  - workflow, com aviso de estado sem caminho até um final;
  - fases e marcos;
  - atividades do projeto e da organização;
  - documentos com versões, e decisões.
- **Markdown seguro (D6):** Markdig com HTML cru desabilitado, esquemas restritos e imagens como links. A Web ganhou uma Content-Security-Policy.
- **Testes (D8):** projeto `ProjectManager.WebTests` com bUnit.
- **Documentação:** ADR-015, `docs/web/TELAS.md`, `docs/api/ORGANIZATIONS.md`, README, AGENTS, ARCHITECTURE, PROJECT_STRUCTURE, especificação e status.

## 4. Arquivos criados

| Área | Arquivos |
|---|---|
| API | Rota `GET /organizations/{id}/me` em `OrganizationsController` (com `MyMembership` no contrato e `GetMyMembershipAsync` no serviço) |
| Web: base | `Api/{GestalynApi,ApiClients,ApiModels,MembershipCache}.cs`, `Display.cs`, `Markdown/MarkdownRenderer.cs`, `Authentication/WebEndpoints.cs`, `wwwroot/js/app.js` |
| Web: componentes | `Components/Shared/{Icon,PageHeader,FlashMessage,Flash,States,EmptyState,ActionForm,StateBadge,ProgressSummary,Pager,MarkdownView,ActivityList,TaskBriefList,OrgFrame,OrgNav,ProjectFrame}`, `Components/Layout/OrgNavHost.razor` |
| Web: páginas | `Pages/OrganizationPage.cs`, `Pages/FormModels.cs`, `Pages/Organizations/*`, `Pages/Projects/*`, `Pages/Tasks/*`, `Pages/Activities/*`, `Pages/Documents/*` |
| Testes | `tests/ProjectManager.WebTests/*` (projeto novo); `IntegrationTests/Organizations/MyMembershipTests.cs` |
| Documentação | `docs/specifications/FASE-09-WEB.md`, `docs/decisions/ADR-015-INTERFACE-WEB.md`, `docs/web/TELAS.md`, este relatório |

## 5. Arquivos modificados

| Arquivo | Alteração |
|---|---|
| `Web/Program.cs` | Clientes, cache de associação, Display, Markdown, CSP, cookie da organização, download de anexos |
| `Web/Api/ApiRequests.cs` | Cabeçalho de organização, enumerações por nome, corpo em respostas 201, upload e resposta crua |
| `Web/Authentication/{TokenRefreshCookieEvents,AccountEndpoints}.cs` | Renovação do token também no download; cookie da organização apagado ao sair |
| `Web/Components/{App.razor,Layout/MainLayout.razor,Layout/ReconnectModal.razor,Pages/Home.razor}` | Sem script inline; novo layout; textos em pt-BR; início com redirecionamento |
| `Web/wwwroot/app.css` | Design system |
| `Web/ProjectManager.Web.csproj`, `Directory.Packages.props`, `ProjectManager.sln` | Markdig, bUnit e o projeto de testes |
| Removidos: `Layout/NavMenu.razor(.css)` e `MainLayout.razor.css` | Substituídos pelo novo layout |
| `README.md`, `AGENTS.md`, `docs/architecture/*`, `docs/api/ORGANIZATIONS.md`, `docs/specifications/README.md`, `docs/PROJECT_STATUS.md` | Documentação da fase |

## 6. Dependências adicionadas

| Pacote | Versão | Licença | Uso | Compatibilidade |
|---|---|---|---|---|
| Markdig | 1.5.1 (publicado em 2026-10-28) | BSD-2-Clause | Renderização do Markdown na Web | Alvo `net10.0` no pacote |
| bunit | 2.13.3 (publicado em 2026-10-25) | MIT | Testes de componentes (só testes) | Alvo `net10.0` no pacote |

## 7. Justificativas técnicas

- **SSR com o token do cookie (D2):** mantém o modelo BFF do ADR-006. Interatividade com a API num circuito exigiria renovar o token fora da requisição, e a rotação do refresh token (ADR-012) trataria isso como reuso.
- **Permissões só da API (D4):** a Web não recalcula regras de acesso. A rota `/me` evita cruzar papéis na Web, e o teste de execução real mostrou que forçar uma ação pela página recebe 403 da API.
- **Quadro pela estrutura do workflow (D5, ajuste):** evita uma chamada por cartão. A regra do próprio trabalho continua na API.
- **Markdig com HTML desabilitado e CSP (D6):** duas camadas contra XSS. Imagens externas não carregam, nem revelam quem abriu a página.
- **Aviso por chave, não texto:** uma URL não consegue fazer a página exibir uma mensagem inventada.
- **Cache de associação por requisição:** o menu e a página usam a mesma chamada à API.

## 8. Testes executados

| Suíte | Novos nesta fase | Conteúdo |
|---|---|---|
| WebTests (`MarkdownRendererTests`) | 5 métodos, 21 casos | Formatação; HTML cru escapado (script, img, iframe, atributos); links `javascript:`, `JavaScript:`, `java\tscript:`, `data:`, `vbscript:` e autolink sem destino; links seguros com `rel`; imagens como links; `IsSafe` com esquemas e caminhos |
| WebTests (`StateComponentTests`) | 5 métodos, 8 casos | Carregamento anunciado; erros 404, 403, 401 e API indisponível no lugar do conteúdo; progresso "Sem dados"; percentual com contagens; selo com nome |
| WebTests (`PagePermissionTests`) | 7 métodos, 9 casos | Menu de atividades; botão de novo projeto; não membro sem dados; configurações do projeto para Observador e Gerente; tarefa para Observador; transições exatamente como a API devolve |
| WebTests (`TaskBoardTests`) | 4 métodos, 5 casos | Colunas na ordem do workflow; sem movimentos sem `tasks.write`; aprovação só com `tasks.approve`; mensagem da API num movimento recusado (409) |
| IntegrationTests (`MyMembershipTests`) | 1 | Papel e permissões do Proprietário e do Leitor; não membro 404; sem autenticação 401 |

## 9. Resultado do build

`dotnet restore`: exit 0. `dotnet build -c Release`: **0 avisos, 0 erros**, com analisadores `Recommended` e avisos tratados como erros.

## 10. Resultado dos testes

| Suíte | Resultado |
|---|---|
| UnitTests | **178/178** |
| WebTests (bUnit) | **43/43** |
| IntegrationTests (PostgreSQL 18 e Mailpit via Testcontainers) | **151/151** |
| Total | **372/372** |

## 11. Evidências

- **Mutações da interface** (todas desfeitas e conferidas):
  1. quadro mostrando transições com aprovação sem `tasks.approve`: 1 teste falha;
  2. Markdown sem `DisableHtml`: 4 testes falham;
  3. arquivamento do projeto sem a permissão: 1 teste falha;
  4. links perigosos mantidos: 5 testes falham.
- **Execução real no navegador** (API e Web locais, banco de desenvolvimento, três usuários reais):

  | Verificação | Resultado |
  |---|---|
  | Entrada e criação de organização pela tela | Redirecionada à visão geral com "Criado com sucesso" |
  | Projeto com descrição maliciosa | `<script>` exibido como texto; link `javascript:` virou `#` |
  | Tarefa com prazo vencido | Destacada como atrasada; datas no fuso de São Paulo |
  | Transição pelos detalhes e pelo quadro | "Tarefa movida"; histórico e colunas atualizados |
  | Workflow: estado novo sem caminho até um final | Aviso exibido; exclusão pede confirmação; cancelar não exclui; confirmar exclui |
  | Convites e equipe pela tela | Dois convites pendentes; colaboradora e observador adicionados com os papéis |
  | Colaboradora | Vê A fazer, Em revisão e Bloqueada (sem as de aprovação); comenta e edita; não arquiva |
  | Aprovação forçada pela página (valor do campo trocado) | API recusou (403), mensagem exibida, estado inalterado |
  | Tokens no navegador | Nenhum cookie legível, nada no HTML nem no armazenamento local; o navegador só falou com a Web |
  | Conflito de versão de documento | 409 com a mensagem; versão do gerente preservada; texto da colaboradora mantido no formulário |
  | Decisão proposta pela colaboradora | Sem "Aceitar" para ela; o gerente aceitou com confirmação; deixou de ser editável |
  | Observador | Nenhum formulário de escrita no quadro, na tarefa, nos documentos e nas configurações |
  | Gerente aprovou a tarefa | "Concluída", com o comentário no histórico; progresso 100% (1 de 1) |
  | Responsividade | 11 telas principais sem rolagem horizontal em 375 px; menu recolhível; quadro em coluna; capturas em 375 px e 1440 px |
  | Teclado | Foco visível; o foco vai ao título depois de navegar; link "Pular para o conteúdo" |
  | Console e logs | Sem erros de CSP nem de servidor |

## 12. Problemas encontrados

1. Nos componentes de campos (projeto, tarefa, decisão), o POST chegava vazio: os campos tinham o nome `Model.X`.
2. O menu da organização sumia nas páginas com streaming rendering, que usavam seções do Blazor.
3. Expressões de coleção em atributos Razor (trilhas) não compilavam.
4. O namespace `Pages.Activity` colidia com `System.Diagnostics.Activity`.
5. Depois de um envio que falhou, o aviso de sucesso anterior aparecia junto do erro.
6. O cookie da última organização sobrevivia ao sair.
7. Um `onchange` inline numa lista seria bloqueado pela CSP.
8. Dois testes tinham asserções que não mediam o comportamento: o "0%" vinha do `width` da barra, e um link com tabulação nem vira link.
9. O Docker Desktop estava parado no início dos testes de integração.

## 13. Problemas corrigidos

1. Parâmetro `Prefix` nos componentes de campos, com o nome do formulário da página.
2. Menu no layout pelo `OrgNavHost`, com a associação compartilhada pelo `MembershipCache`.
3. Expressões explícitas `@(...)` nas trilhas.
4. Pasta renomeada para `Pages/Activities`.
5. O aviso só aparece em requisições GET.
6. Sair apaga o cookie.
7. Inline removido; o filtro usa um botão.
8. As asserções passaram a medir o texto exibido, o `aria-valuetext` e os atributos `href`/`src`.
9. Docker iniciado.

## 14. Pendências

- Push de `feature/fase-09-web`, CI no GitHub e PR para `develop` (dependem de autorização).
- **Fase 10:**
  - interface de agentes, credenciais e relatórios de trabalho (F09-018 transferida);
  - reavaliar o tempo real (F09-021).
- **Fase 11:** a CLI envia `X-Gestalyn-Client: cli`.
- **Fase 12:** tela de integrações.
- **Fase 13:**
  - testes de ponta a ponta com navegador (Playwright);
  - revisão com leitor de tela;
  - medição do quadro, que faz uma chamada por coluna.
- **Melhoria possível:** arrastar e soltar no quadro, com alternativa por teclado.

## 15. Critérios atendidos

| Critério | Evidência |
|---|---|
| Todas as telas principais funcionais | Execução real de cada tela com dados criados pela interface; [TELAS.md](../web/TELAS.md) |
| Interface conectada à API | Toda leitura e gravação passa pelos clientes da API; o navegador só fala com a Web |
| Nenhum dado fictício apresentado como real | Estados vazios; nenhuma tela sem API (agentes transferidos para a Fase 10) |
| Erros tratados | `StateComponentTests`, `ARejectedMove_ShowsTheApiMessage_AndKeepsTheBoard`; execução real com 403 e 409 |
| Permissões refletidas na interface e validadas no backend | `PagePermissionTests`, `TaskBoardTests`, mutações 1 e 3; execução real com três papéis e a aprovação forçada recusada pela API |
| Responsividade validada | Nenhuma rolagem horizontal em 375 px nas 11 telas principais; capturas em 375 px e 1440 px |
| Navegação consistente | Mesmo layout, trilha, abas e padrões de ação em todas as telas; teclado com foco visível |

## 16. Critérios não atendidos

Nenhum critério de aceitação deixou de ser atendido.

Duas tarefas da lista da fase ficaram de fora, com a aprovação de D2 e D7 e o registro no ADR-015:
- **F09-018 (interface de agentes):** transferida para a Fase 10, porque a API de agentes não existe;
- **F09-021 (tempo real):** não implementada, por falta de necessidade comprovada.

A definição de conclusão ainda não foi cumprida: faltam o CI no GitHub e a integração em `develop` por PR. Por isso, a fase fica **In Review**.

## 17. Riscos identificados

| Risco | Impacto | Mitigação |
|---|---|---|
| Interface SSR menos fluida que uma interativa | Experiência | Navegação e formulários aprimorados, streaming, avisos; reavaliar interatividade com um provedor de token por circuito |
| Quadro com uma chamada por coluna | Latência com muitos estados | Até 50 tarefas por coluna; medir na Fase 13 |
| Quadro oferece um movimento que a regra do próprio trabalho recusa | Mensagem de erro em vez de esconder a opção | A API recusa com a explicação; os detalhes da tarefa já filtram |
| Markdown malicioso | XSS | HTML cru desabilitado, esquemas restritos, CSP; testes com conteúdo malicioso e mutações |
| Origem da Web declarada (`X-Gestalyn-Client`) | Estatística imprecisa | Informativa por desenho (ADR-014) |
| Duas dependências novas | Manutenção | Versões fixas no Central Package Management, licenças permissivas, uma delas só de teste |

## 18. Estado do Git

- **Branch:** `feature/fase-09-web`, a partir de `develop` `67144d1`, ainda não enviada.
- **Commits:**
  - `a6c5c07` (especificação);
  - `25d3a27` (interface web e rota `/me`);
  - `a7955b0` (testes de componentes);
  - `409d642` (aviso só na leitura e cookie apagado ao sair);
  - commit de documentação de encerramento.
- **`develop`:** `67144d1`.

## 19. Próxima fase recomendada

1. **Antes:** push, CI e PR da Fase 09 para `develop`, com autorização do responsável.
2. **Fase 10, API e autenticação de agentes:**
   - credenciais de agente com permissões limitadas e revogação;
   - autor `ActorKind.Agent` nos eventos e no histórico;
   - relatórios de trabalho (seção 6.4);
   - o teste de integração "agentes não aprovam o próprio trabalho";
   - as telas de agentes e credenciais transferidas da Fase 09.
