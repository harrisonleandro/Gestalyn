# ADR-001 — Monólito modular em camadas

- **Status:** Aceito
- **Data:** 2026-11-10
- **Fase:** 01 — Fundação .NET

## Contexto

O ProjectManager será uma plataforma multiusuário e multi-organização, hospedada em nuvem e preparada para comercialização, em que pessoas e agentes de IA colaboram nos mesmos projetos. Os clientes previstos são uma interface Web, uma CLI (`projectctl`), um servidor MCP para agentes e integrações como GitHub.

O projeto está no início, com equipe pequena e domínio ainda em consolidação (a especificação v1.0 não estava disponível no repositório na Fase 01). A arquitetura precisa:

1. Permitir evoluir o modelo de domínio rapidamente, sem custo de coordenação entre serviços.
2. Manter fronteiras claras entre regras de negócio e detalhes técnicos.
3. Garantir que todos os clientes (humanos e agentes) passem pelas mesmas regras de autorização e isolamento de tenant.
4. Não impedir a extração futura de módulos para serviços independentes, se a escala exigir.

## Decisão

Adotar um **monólito modular** em .NET 10 com camadas inspiradas em Clean Architecture:

```
Application → Domain
Infrastructure → Application + Domain
API → Application + Infrastructure
Web → API (somente HTTP)
CLI → API (somente HTTP)
```

- Um único processo de backend (`ProjectManager.API`) com um único banco PostgreSQL (fase futura).
- Módulos de negócio organizados verticalmente dentro das camadas, sem acesso direto aos dados de outro módulo.
- Web e CLI são **clientes HTTP** da API e não referenciam projetos internos.
- O futuro servidor MCP acessa os casos de uso pela camada `Application` ou por contratos próprios, sem duplicar regras de negócio (veja a revisão abaixo).
- As regras de dependência são **verificadas por testes automatizados** (`ProjectReferenceRulesTests`), não apenas documentadas.
- `ProjectManager.MCP` e `ProjectManager.Worker` serão introduzidos em fases posteriores, seguindo as mesmas regras.

## Alternativas consideradas

| Alternativa | Motivo da rejeição |
|---|---|
| Microsserviços desde o início | Custo operacional (deploy, observabilidade, consistência distribuída) desproporcional ao estágio do produto; fronteiras de domínio ainda não validadas. |
| Monólito sem camadas (projeto único) | Mistura regras de negócio com detalhes de infraestrutura; dificulta testes e a futura extração de módulos. |
| Web referenciando `Application` diretamente (Blazor Server "in-process") | Criaria um segundo caminho de acesso a dados, fora das regras de autorização e tenant da API, e acoplaria a implantação da Web ao backend. |

## Consequências

**Positivas**
- Deploy, depuração e transações simples (um processo, um banco).
- Fronteiras explícitas e verificadas automaticamente.
- API como ponto único de autorização e isolamento multi-tenant para todos os clientes, incluindo agentes de IA.
- Caminho aberto para extrair módulos no futuro.

**Negativas / riscos**
- A Web faz uma chamada HTTP adicional (servidor a servidor) em vez de chamar a camada de aplicação em memória.
- A disciplina modular depende de convenções e testes; sem eles, o monólito tende a se acoplar.
- Escala horizontal ocorre pela aplicação inteira, não por módulo.

## Revisão de 2026-11-10: alinhamento com a especificação mestre

A versão original deste ADR foi escrita sem uma especificação e afirmava que o servidor MCP seria um cliente HTTP da API. A [especificação mestre](../specifications/ESPECIFICACAO-MESTRE.md), seção 3.1, define:

- **MCP:** utiliza a camada Application ou contratos próprios para acessar os casos de uso, sem duplicar regras de negócio.
- **Worker:** utiliza abstrações da Application e implementações da Infrastructure.

O ADR foi ajustado para refletir essas regras. A escolha concreta entre referenciar `Application` ou consumir a API (contrato próprio) será registrada em ADR na Fase 12, depois do estudo da especificação MCP vigente. Em qualquer caso, autenticação, autorização e isolamento multi-tenant devem ser os mesmos aplicados pela API.

A especificação mestre também confirma a estratégia de multi-tenancy: banco e schema compartilhados, `OrganizationId`, UUID e validação no backend.

## Conformidade

- `tests/ProjectManager.UnitTests/Architecture/ProjectReferenceRulesTests.cs` falha se um projeto em `src/` referenciar um projeto não permitido ou se um novo projeto não estiver coberto pelas regras.
- Alterações nesta decisão exigem um novo ADR que a substitua.
