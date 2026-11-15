# ADR-015: Interface web completa

- **Status:** Aceito
- **Data:** 2026-11-15
- **Fase:** 09 (Interface web completa). Decisões D1 a D8 aprovadas pelo responsável em 2026-11-15.

## Contexto

A Fase 09 constrói a experiência de uso na Web (Blazor) sobre a API das Fases 03 a 08. A especificação mestre (seção 8) pede:
- tema escuro, interface profissional, responsiva e acessível;
- estados de carregamento e de erro, e confirmação de ações destrutivas;
- permissões refletidas na interface e validadas no backend;
- nenhum dado fictício apresentado como real.

A Web já era BFF (ADR-006): os tokens da API ficam num cookie HttpOnly, renovado no servidor, e as páginas são renderizadas no servidor.

## Decisão

### Design system (D1)

- **Base:** Bootstrap 5.3, que já estava no projeto, no tema escuro, com tokens próprios em `wwwroot/app.css` (superfícies, texto, destaque, cores das categorias, raios e foco).
- **Componentes Razor reutilizáveis** (`Components/Shared`):
  - estrutura da página: `PageHeader` (trilha, título e ações), `OrgFrame` e `ProjectFrame` (molduras com abas);
  - estados: `States` (carregando, erro por código) e `EmptyState`;
  - ações: `ActionForm` (ação de um clique com confirmação opcional) e `FlashMessage`;
  - dados: `StateBadge`, `ProgressSummary`, `Pager`, `MarkdownView`, `ActivityList` e `TaskBriefList`;
  - `Icon`, com ícones em SVG embutido.
- **Nenhuma dependência nova de interface.**

### Renderização (D2)

- **Páginas SSR estáticas** com navegação e formulários aprimorados (`Enhance`, `data-enhance`), e streaming rendering nas páginas de leitura.
- **Token por requisição:** o `GestalynApi` obtém o token pelo `IApiRequestContext`, que o lê do cookie da requisição. O token nunca chega ao navegador.
- **Ações:** cada ação é um formulário com antiforgery. Em sucesso, a página redireciona para si mesma com um aviso (post-redirect-get).
  - O aviso é uma **chave conhecida** na URL (`?aviso=salvo`), nunca texto livre. Ele só aparece na leitura da página, não junto de um erro de envio.
- **JavaScript próprio:** um módulo (`wwwroot/js/app.js`) que abre o diálogo nativo `<dialog>` antes de enviar formulários com `data-confirm`. O menu em telas pequenas funciona só com CSS.
- **Sem SignalR (F09-021):** nenhuma tela tem necessidade comprovada de atualização em tempo real. Reavaliar quando houver uma, por exemplo com agentes trabalhando ao mesmo tempo (Fase 10).

### Organização ativa (D3)

- **Na URL:** `/o/{organizationId}/...`, como na API.
- **Associação:** é confirmada por `GET /organizations/{id}/me`. Quem não é membro vê "não encontrado".
- **Cache por requisição:** o menu do layout (`OrgNavHost`) e a página usam a mesma resposta (`MembershipCache`), sem chamada duplicada.
- **Última organização:** fica num cookie HttpOnly só com o ID (`.Gestalyn.Organization`), para o redirecionamento depois de entrar. Sair apaga esse cookie.

### Permissões na interface (D4)

- **Rota nova** `GET /api/v1/organizations/{id}/me`: devolve o papel e as permissões do usuário na organização.
  - Basta ser membro; não muda nenhuma regra.
  - Tem teste de integração: papéis diferentes, não membro (404) e sem autenticação (401).
- **O que a Web mostra** depende só do que a API devolve:
  - `permissions` (organização), `myPermissions` (projeto e tarefa) e `availableTransitions` (tarefa);
  - no quadro, a estrutura do workflow filtrada pelas permissões do projeto (veja D5).
- **Toda ação é validada no backend.** A Web trata 400, 401, 403, 404 e 409 com a mensagem da API.
  - Na execução real, forçar uma aprovação sem permissão pela própria página recebeu 403 da API, e nada mudou.

### Quadro (D5)

- **Colunas:** uma por estado do workflow, na ordem configurada, com a contagem e as 50 primeiras tarefas. Um link leva à lista filtrada pelo estado.
- **"Mover para…":** oferece as transições que saem do estado na estrutura do workflow, com `expectedStateId` contra movimentos simultâneos.
  - As que exigem aprovação só aparecem para quem tem `tasks.approve` no projeto.
- **Ajuste em relação à proposta:** a listagem da API não traz as transições disponíveis de cada tarefa. Pedir os detalhes de cada cartão custaria uma chamada por tarefa.
  - Por isso, o quadro usa a estrutura do workflow mais as permissões do projeto.
  - A regra do próprio trabalho (aprovação independente) continua só na API: o quadro não a repete, e a API recusa com 403 e a mensagem.
  - Os detalhes da tarefa usam `availableTransitions`, que já vem filtrado.

### Markdown (D6)

- **Markdig 1.5.1** (licença BSD-2, compatível com .NET 10).
  - HTML cru desabilitado (`DisableHtml`).
  - Links só para `http`, `https`, `mailto` e caminhos relativos; os demais viram `#`.
  - Imagens viram links: a CSP não carregaria imagens de fora, e carregá-las revelaria quem abriu a página.
  - `rel="noopener noreferrer nofollow"` nos links.
- **Content-Security-Policy da Web:** `default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; frame-ancestors 'none'; form-action 'self'` e demais diretivas.
  - O `<ImportMap>` embutido saiu do `App.razor`, porque a CSP não aceita scripts inline.
  - A Web envia também `X-Content-Type-Options: nosniff` e `Referrer-Policy: same-origin`.

### Escopo e o conflito com a Fase 10 (D7)

- **Telas transferidas:** a interface de agentes (F09-018) e as telas "Agentes", "Credenciais" e "Integrações" (seção 8.1) passam para as Fases 10 e 12, quando a API delas existir.
  - O menu não mostra entradas para elas, e nenhuma tela fictícia foi criada.
- **Configurações (F09-017):**
  - da organização: nome, membros, papéis e convites;
  - do projeto: dados, equipe, etiquetas, repositórios, arquivamento e workflow;
  - o perfil, que já existia.
- **Acessibilidade e responsividade:**
  - link "Pular para o conteúdo" e foco visível;
  - rótulos em todos os campos;
  - cor nunca é a única informação: o selo traz o nome do estado;
  - `aria-live` no carregamento;
  - de 360 px a 1920 px: menu recolhível, tabelas que viram cartões e quadro em coluna única em telas pequenas.

### Testes (D8)

- **Projeto novo `tests/ProjectManager.WebTests`** (bUnit 2.13.3, licença MIT), que referencia só a Web.
  - A rede é substituída por uma API falsa (`StubApiHandler`); clientes, `Display` e Markdown são os reais.
  - Cobre:
    - o Markdown seguro;
    - os estados;
    - as permissões refletidas (menu, novo projeto, configurações do projeto, tarefa, quadro, aprovação);
    - o quadro;
    - a mensagem de um movimento recusado (409).
- **Execução real no navegador:** gerente, colaboradora e observador, com capturas em 375 px e 1440 px e a conferência de que nenhum token chega ao navegador.

### Decisões de baixo risco

- **Clientes da API na Web:** `GestalynApi` mais um cliente por área (`OrganizationsApi`, `ProjectsApi`, `PlanningApi`, `TasksApi`, `WorkflowApi`, `DocumentsApi`), com DTOs próprios em `Api/ApiModels.cs`. A Web não referencia Domain nem Application.
  - A Web envia `X-Gestalyn-Client: web` (ADR-014) e `X-Gestalyn-Organization` nas rotas por ID.
- **Datas:** com hora, no fuso `Display:TimeZone` (padrão `America/Sao_Paulo`); prazos sem fuso; formato pt-BR.
- **Anexos:**
  - envio por formulário `multipart` com antiforgery;
  - download por `/o/{org}/tarefas/{task}/anexos/{id}`, que repassa o arquivo da API como anexo e renova o token antes (`ApiTokenEndpointMetadata`).
- **Campos em componentes filhos:** usam o nome da propriedade do formulário da página (`Prefix`), para o binding do POST.

## Alternativas consideradas

| Alternativa | Motivo da rejeição |
|---|---|
| MudBlazor ou Fluent UI | Pacote grande, estilo próprio, acoplado à interatividade |
| Interactive Server nas telas | Tokens no circuito; a rotação do refresh token no circuito conflitaria com o cookie e dispararia a detecção de reuso |
| WebAssembly | Tokens no navegador, proibido pelo ADR-006 |
| Organização só no cookie | URLs que não se compartilham; abas em organizações diferentes se atrapalhariam |
| Calcular permissões na Web a partir dos papéis | Duplicaria a regra de acesso fora do backend |
| Arrastar e soltar no quadro | Exige interatividade com a API e uma alternativa por teclado; pode vir depois |
| Markdown como texto puro | Seguro, mas perde a formatação |
| Sanitizador de HTML com HTML liberado | Duas dependências e mais superfície |
| Telas de agentes "em breve" | Telas que aparentam existir sem implementação contrariam a especificação |
| Seções do Blazor (`SectionContent`) para o menu | Com streaming rendering, o conteúdo da seção enviado depois não atualizava o layout; o menu sumia nas páginas com streaming |

## Consequências

- **Fase 10:** a interface de agentes, as credenciais e os relatórios de trabalho entram com a API de agentes. Reavaliar o SignalR se houver atualizações concorrentes frequentes.
- **Fase 11:** a CLI envia `X-Gestalyn-Client: cli`.
- **Fase 12:** a tela de integrações.
- **Fase 13:**
  - testes de ponta a ponta com navegador (Playwright);
  - medição do quadro, que faz uma chamada por coluna;
  - revisão de acessibilidade com leitor de tela.
- **Telas novas:** devem usar os componentes do design system e as permissões devolvidas pela API, nunca regras próprias.
