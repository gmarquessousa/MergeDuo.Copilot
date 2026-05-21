# MergeDuo.Copilot

API .NET 8 criada para integrar o MergeDuo ao Copilot Studio. Ela expoe endpoints anonimos para consulta financeira, lendo diretamente do Cosmos DB, sem chamar APIs HTTP de outros microservicos.

## Objetivo

O servico foi desenhado para o Copilot Studio conseguir responder perguntas como:

- "Me fale o resumo do mes de 05/2026"
- "Faca um levantamento dos proximos 3 meses"
- "Simule uma compra de R$ 3.500 no cartao em 6 vezes"
- "Quais cartoes estao disponiveis?"

A API usa um usuario fixo configurado por ambiente em `Copilot__UserId`. Se esse usuario tiver merge ativo no container `partnerships`, as respostas somam usuario principal e parceiro.

## Arquitetura

Este projeto e standalone. Ele nao depende de `MergeDuo.Aggregates` em runtime, build ou Docker.

Estrutura:

```text
src/
  MergeDuo.Copilot.Api      Minimal API, Swagger, rate limit, health checks
  MergeDuo.Copilot.Domain   Contratos, regras financeiras e servicos de dominio
  MergeDuo.Copilot.Infra    Leitura direta do Cosmos DB
tests/
  MergeDuo.Copilot.Tests    Testes de API e dominio com repositorio em memoria
```

Containers Cosmos usados:

- `users`
- `partnerships`
- `monthlyAggregates`
- `transactions`
- `fixedRules`
- `cards`

## Seguranca atual

No v1, os endpoints de `/copilot` nao exigem `Authorization`, token ou header.

Isso foi feito para simplificar o teste com Copilot Studio. Em producao, o acesso aos dados fica limitado apenas pelo `Copilot__UserId` configurado no ambiente.

Importante:

- Qualquer pessoa com a URL publica consegue chamar a API.
- Nao exponha essa API anonima com dados reais sem controle de rede, rate limit e revisao de seguranca.
- A connection string do Cosmos deve ser configurada como secret no Azure Container Apps.
- Para uma versao final, recomenda-se adicionar autenticacao, validacao de origem ou uma camada de API Management.

Protecoes existentes:

- Rate limit por IP.
- Limite de payload em 16 KB.
- API read-only.
- Simulacao de compra nao persiste dados.
- `readyz` valida configuracao, containers e existencia do usuario fixo.

## Configuracao

Use variaveis de ambiente com `__` para representar secoes do `appsettings`.

Obrigatorias:

```text
Copilot__UserId=usr_xxx
Copilot__BusinessTimeZone=America/Sao_Paulo

Cosmos__Database=mergeduo
Cosmos__UsersContainer=users
Cosmos__PartnershipsContainer=partnerships
Cosmos__MonthlyAggregatesContainer=monthlyAggregates
Cosmos__TransactionsContainer=transactions
Cosmos__FixedRulesContainer=fixedRules
Cosmos__CardsContainer=cards
```

Opcionais:

```text
Copilot__SafetyMarginValue=500
```

Escolha uma forma de conexao com Cosmos:

```text
Cosmos__ConnectionString=AccountEndpoint=...;AccountKey=...;
```

ou:

```text
Cosmos__Endpoint=https://cdb-mergeduo.documents.azure.com:443/
```

Se usar `Cosmos__Endpoint`, o container app precisa de Managed Identity com permissao adequada no Cosmos DB.

Opcionais:

```text
Copilot__SourceVersion=4
Copilot__ProjectionMonths=3
Copilot__MaxSimulationInstallments=48

RateLimit__GlobalPermitLimit=120
RateLimit__ReadPermitLimit=60
RateLimit__SimulationPermitLimit=30
```

Ambiente ASP.NET:

```text
ASPNETCORE_ENVIRONMENT=Production
```

Use `Development` somente para teste, principalmente se quiser acessar Swagger publico. Em `Production`, o Swagger nao e habilitado.

## Rodar localmente

Na raiz do repositorio `MergeDuo.Copilot`:

```powershell
$env:ASPNETCORE_ENVIRONMENT="Development"
$env:Copilot__UserId="usr_xxx"
$env:Copilot__BusinessTimeZone="America/Sao_Paulo"
$env:Copilot__SafetyMarginValue="500"
$env:Cosmos__ConnectionString="AccountEndpoint=...;AccountKey=...;"
$env:Cosmos__Database="mergeduo"
$env:Cosmos__UsersContainer="users"
$env:Cosmos__PartnershipsContainer="partnerships"
$env:Cosmos__MonthlyAggregatesContainer="monthlyAggregates"
$env:Cosmos__TransactionsContainer="transactions"
$env:Cosmos__FixedRulesContainer="fixedRules"
$env:Cosmos__CardsContainer="cards"

dotnet run --project .\src\MergeDuo.Copilot.Api --urls "http://localhost:5088"
```

Health checks:

```powershell
Invoke-RestMethod http://localhost:5088/
Invoke-RestMethod http://localhost:5088/startupz
Invoke-RestMethod http://localhost:5088/healthz
Invoke-RestMethod http://localhost:5088/readyz
```

`/` e `startupz` sao endpoints baratos para startup probe e nao acessam Cosmos. `healthz` valida se o processo esta rodando. `readyz` valida configuracao, containers Cosmos e existencia do usuario configurado em `Copilot__UserId`.

## Testar com tunel

O Copilot Studio roda na nuvem e nao consegue chamar `localhost` diretamente. Para testar a API local no Copilot Studio, exponha a porta local com uma URL HTTPS publica.

Exemplo com ngrok:

```powershell
ngrok http 5088
```

Se o ngrok retornar:

```text
https://abc123.ngrok-free.app
```

Use:

```text
https://abc123.ngrok-free.app/startupz
https://abc123.ngrok-free.app/healthz
https://abc123.ngrok-free.app/readyz
https://abc123.ngrok-free.app/swagger/v1/swagger.json
```

Enquanto o tunel estiver aberto, qualquer pessoa com a URL consegue chamar a API.

## Endpoints

### GET /copilot/month-summary/{year}/{month}

Retorna o resumo financeiro de um mes.

Exemplo:

```http
GET /copilot/month-summary/2026/5
```

Com PowerShell:

```powershell
Invoke-RestMethod http://localhost:5088/copilot/month-summary/2026/5
```

Comportamento:

- Usa `monthlyAggregates` quando existir agregado para o mes.
- Se nao existir agregado, calcula uma resposta transitoria em memoria a partir de `transactions` e `fixedRules`.
- Nao grava nenhum documento no Cosmos.
- Se houver merge ativo, soma usuario principal e parceiro.

Resposta inclui:

- `scope`: `single` ou `merged`
- `owners`
- `period`
- `totals`
- `byCategory`
- `byCard` como mapa legado por id do cartao
- `cardsSummary` com nome do cartao, responsavel, vencimento, valor e percentuais
- `categoriesSummary` com labels, percentuais, confirmado/projetado e principais movimentos
- `ownersSummary` com resumo por responsavel
- `financialRatios`
- `confirmedVsProjected`
- `dailyCashflow`
- `cashflowMetrics`
- `commitmentCalendar`
- `highlights`
- `comparison`
- `threeMonthAverage`
- `againstThreeMonthAverage`
- `relevantMovements` enriquecido com `ownerName`, `cardTitle`, `categoryLabel` e `kindLabel`
- `dataFreshness`
- `computedAt`
- `summaryText`
- `aiContextText`

O endpoint retorna dados objetivos e contexto factual para IA. Ele nao retorna recomendacao, julgamento de risco ou decisao financeira.

### GET /copilot/next-three-months

Retorna o mes informado inteiro mais os dois meses seguintes.

Exemplo:

```http
GET /copilot/next-three-months?year=2026&month=5
```

Com PowerShell:

```powershell
Invoke-RestMethod "http://localhost:5088/copilot/next-three-months?year=2026&month=5"
```

Se `year` e `month` forem omitidos, usa o mes atual no fuso configurado em `Copilot__BusinessTimeZone`.

Resposta inclui:

- totais consolidados da janela
- lista de meses
- patrimonio projetado no ultimo mes
- proximos movimentos relevantes
- `summaryText`

### POST /copilot/purchase-simulation

Simula o impacto de uma compra sem persistir nada.

Compra a vista:

```powershell
Invoke-RestMethod `
  -Method Post `
  -Uri http://localhost:5088/copilot/purchase-simulation `
  -ContentType "application/json" `
  -Body '{
    "description": "Mesa",
    "amount": 250,
    "purchaseDate": "2026-05-22",
    "paymentType": "cash"
  }'
```

Compra no cartao:

```powershell
Invoke-RestMethod `
  -Method Post `
  -Uri http://localhost:5088/copilot/purchase-simulation `
  -ContentType "application/json" `
  -Body '{
    "description": "Notebook",
    "amount": 3500,
    "purchaseDate": "2026-05-21",
    "paymentType": "credit_card",
    "cardId": "card_x",
    "installments": 6
  }'
```

Campos:

```json
{
  "description": "Notebook",
  "amount": 3500,
  "purchaseDate": "2026-05-21",
  "paymentType": "credit_card",
  "cardId": "card_x",
  "installments": 6
}
```

Regras:

- `paymentType` aceita `cash` ou `credit_card`.
- Para `cash`, o impacto ocorre em `purchaseDate`.
- Para `credit_card`, `cardId` e obrigatorio.
- Para `credit_card`, a API usa `closingDay` e `dueDay` do container `cards`.
- Parcelas sao distribuidas por vencimento de fatura.
- Nenhuma transacao e criada.

### GET /copilot/cards

Lista os cartoes ativos disponiveis para o usuario configurado no Copilot. Se houver merge ativo, lista tambem os cartoes do parceiro.

Exemplo:

```http
GET /copilot/cards
```

Com PowerShell:

```powershell
Invoke-RestMethod http://localhost:5088/copilot/cards
```

Resposta inclui:

- `scope`: `single` ou `merged`
- `owners`
- `cards`
- `computedAt`
- `summaryText`

Cada item em `cards` inclui:

- `id`: id do cartao, usado em simulacoes
- `title`: nome do cartao
- `ownerUserId`: usuario responsavel pelo cartao
- `ownerRole`: `primary` ou `partner`
- `ownerName`: nome do usuario responsavel pelo cartao
- `closingDay`: dia de fechamento
- `dueDay`: dia de vencimento
- `nextDueDate`: proxima data de vencimento calculada pelo fuso do negocio
- `currency`

Exemplo de resposta:

```json
{
  "scope": "merged",
  "owners": [
    { "userId": "usr_primary", "role": "primary" },
    { "userId": "usr_partner", "role": "partner" }
  ],
  "cards": [
    {
      "id": "card_nubank",
      "title": "Nubank",
      "ownerUserId": "usr_primary",
      "ownerRole": "primary",
      "ownerName": "Gavriel",
      "closingDay": 28,
      "dueDay": 10,
      "nextDueDate": "2026-06-10",
      "currency": "BRL"
    }
  ],
  "computedAt": "2026-05-21T15:00:00Z",
  "summaryText": "1 cartao(oes) ativo(s) encontrado(s) considerando o merge ativo."
}
```

## Swagger e Copilot Studio

Em `Development`, o OpenAPI fica em:

```text
http://localhost:5088/swagger/v1/swagger.json
```

Com tunel:

```text
https://abc123.ngrok-free.app/swagger/v1/swagger.json
```

Operation IDs expostos:

- `GetMonthSummary`
- `GetNextThreeMonths`
- `ListAvailableCards`
- `SimulatePurchase`

No Copilot Studio:

1. Abra o agente.
2. Va em `Tools`.
3. Adicione uma tool por REST API ou Custom Connector.
4. Informe a URL do OpenAPI.
5. Configure autenticacao como `No authentication`.
6. Selecione as operacoes desejadas.
7. Teste perguntas como "Me fale o resumo do mes de 05/2026".

Para ambiente publicado, prefira usar a URL HTTPS do Azure Container Apps.

## Azure Container Apps

### Porta

A imagem escuta em:

```text
8080
```

Configure o ingress target port do ACA como `8080`.

### Probes no ACA

Use probes que separam startup, liveness e readiness:

```text
Startup probe
Type: HTTP GET
Path: /startupz
Port: 8080

Liveness probe
Type: HTTP GET
Path: /healthz
Port: 8080

Readiness probe
Type: HTTP GET
Path: /readyz
Port: 8080
```

Nao use `/readyz` como startup probe. O `/readyz` acessa Cosmos e valida o usuario fixo; se Cosmos, firewall, secret ou `Copilot__UserId` ainda estiverem incorretos, o ACA pode reiniciar a revision antes de voce conseguir diagnosticar a aplicacao.

Se estiver configurando pelo portal:

- `Ingress > Target port`: `8080`
- `Containers > Health probes > Startup`: `HTTP`, path `/startupz`, port `8080`
- `Containers > Health probes > Liveness`: `HTTP`, path `/healthz`, port `8080`
- `Containers > Health probes > Readiness`: `HTTP`, path `/readyz`, port `8080`

Se a revision estiver em loop durante o primeiro deploy, remova temporariamente a readiness probe ou mantenha apenas startup/liveness ate validar as variaveis de ambiente e o acesso ao Cosmos.

### Variaveis de ambiente no ACA

No Azure Container Apps, nao use `$env:`. Essa sintaxe e apenas para PowerShell local.

No ACA, configure:

```text
ASPNETCORE_ENVIRONMENT=Production
Copilot__UserId=usr_xxx
Copilot__BusinessTimeZone=America/Sao_Paulo
Copilot__SafetyMarginValue=500
Cosmos__Database=mergeduo
Cosmos__UsersContainer=users
Cosmos__PartnershipsContainer=partnerships
Cosmos__MonthlyAggregatesContainer=monthlyAggregates
Cosmos__TransactionsContainer=transactions
Cosmos__FixedRulesContainer=fixedRules
Cosmos__CardsContainer=cards
```

Connection string como secret:

```bash
az containerapp secret set \
  -n <ACA_NAME> \
  -g <RESOURCE_GROUP> \
  --secrets cosmos-connection-string="<COSMOS_CONNECTION_STRING>"
```

Referencia do secret em env var:

```bash
az containerapp update \
  -n <ACA_NAME> \
  -g <RESOURCE_GROUP> \
  --set-env-vars \
    ASPNETCORE_ENVIRONMENT=Production \
    Copilot__UserId=usr_xxx \
    Copilot__BusinessTimeZone=America/Sao_Paulo \
    Copilot__SafetyMarginValue=500 \
    Cosmos__ConnectionString=secretref:cosmos-connection-string \
    Cosmos__Database=mergeduo \
    Cosmos__UsersContainer=users \
    Cosmos__PartnershipsContainer=partnerships \
    Cosmos__MonthlyAggregatesContainer=monthlyAggregates \
    Cosmos__TransactionsContainer=transactions \
    Cosmos__FixedRulesContainer=fixedRules \
    Cosmos__CardsContainer=cards
```

Toda alteracao de environment variables cria ou exige uma nova revision.

### Validacao no ACA

Depois do deploy:

```bash
curl https://<APP_URL>/startupz
curl https://<APP_URL>/healthz
curl https://<APP_URL>/readyz
curl https://<APP_URL>/copilot/month-summary/2026/5
```

Se `readyz` falhar, a API ainda pode estar de pe, mas alguma dependencia obrigatoria nao esta pronta.

## Docker

O contexto do build deve ser a raiz do repositorio `MergeDuo.Copilot`.

```bash
docker build -f src/MergeDuo.Copilot.Api/Dockerfile -t mergeduo-copilot:local .
```

Rodar container local:

```bash
docker run --rm -p 5088:8080 \
  -e ASPNETCORE_ENVIRONMENT=Production \
  -e Copilot__UserId=usr_xxx \
  -e Copilot__BusinessTimeZone=America/Sao_Paulo \
  -e Copilot__SafetyMarginValue=500 \
  -e Cosmos__ConnectionString="AccountEndpoint=...;AccountKey=...;" \
  -e Cosmos__Database=mergeduo \
  -e Cosmos__UsersContainer=users \
  -e Cosmos__PartnershipsContainer=partnerships \
  -e Cosmos__MonthlyAggregatesContainer=monthlyAggregates \
  -e Cosmos__TransactionsContainer=transactions \
  -e Cosmos__FixedRulesContainer=fixedRules \
  -e Cosmos__CardsContainer=cards \
  mergeduo-copilot:local
```

## GitHub Actions

O workflow publica a imagem no ACR usando Docker Buildx.

Variaveis esperadas no GitHub Environment `environment`:

```text
ACR_NAME
ACR_LOGIN_SERVER
AZURE_RESOURCE_GROUP
ACA_NAME
IMAGE_REPOSITORY
DOCKERFILE
DOCKER_CONTEXT
ACA_KIND
```

Valores esperados para este repositorio:

```text
DOCKER_CONTEXT=.
DOCKERFILE=src/MergeDuo.Copilot.Api/Dockerfile
ACA_KIND=app
IMAGE_REPOSITORY=mergeduo-copilot
```

Secrets esperados:

```text
AZURE_CLIENT_ID
AZURE_TENANT_ID
AZURE_SUBSCRIPTION_ID
```

O deploy/update do Azure Container Apps pode ser reativado no workflow quando desejado. Atualmente a etapa de update pode estar comentada conforme a configuracao do repositorio.

## Testes

Rodar tudo:

```bash
dotnet test MergeDuo.Copilot.sln
```

Release:

```bash
dotnet build MergeDuo.Copilot.sln -c Release
dotnet test MergeDuo.Copilot.sln -c Release --no-build
```

Coberturas principais:

- resumo mensal de usuario unico
- resumo mensal com merge ativo
- fallback sem `monthlyAggregates`
- levantamento de 3 meses
- simulacao cash sem persistencia
- simulacao cartao parcelado com fechamento/vencimento
- endpoints anonimos
- `readyz` quando usuario nao existe
- Swagger com operationIds esperados

## Troubleshooting

### ACA mostra `Probe of StartUp failed with status code: 1`

Isso normalmente indica que o Azure Container Apps nao conseguiu validar a startup probe e reiniciou a revision. Confira:

- O ingress target port deve ser `8080`.
- A startup probe deve ser `HTTP GET /startupz` na porta `8080`.
- A liveness probe deve ser `HTTP GET /healthz` na porta `8080`.
- Nao use `/readyz` como startup probe.
- Se houver uma startup probe do tipo comando/exec, remova ou troque por HTTP GET.

O stream de eventos do ACA mostra falhas de infraestrutura/probe. Para ver o erro real do .NET, use o console log stream:

```bash
az containerapp logs show \
  -n <ACA_NAME> \
  -g <RESOURCE_GROUP> \
  --follow
```

Depois de corrigir probes ou environment variables, crie uma nova revision.

### `readyz` retorna `missing_copilot_user_id`

`Copilot__UserId` nao foi configurado ou nao segue o padrao `usr_...`.

### `readyz` retorna `copilot_user_not_found`

O usuario configurado em `Copilot__UserId` nao existe no container `users`, ou esta marcado com `deletedAt`.

### `readyz` retorna `copilot_dependency_unavailable`

A API nao conseguiu acessar algum container do Cosmos. Verifique connection string, endpoint, firewall, permissao de managed identity e nomes dos containers.

### `readyz` retorna `copilot_readiness_timeout`

A validacao do Cosmos demorou demais. Verifique conectividade, firewall, regiao, DNS e disponibilidade do Cosmos.

### Swagger nao abre na ACA

Se `ASPNETCORE_ENVIRONMENT=Production`, Swagger fica desabilitado. Para teste temporario, use:

```text
ASPNETCORE_ENVIRONMENT=Development
```

Depois volte para `Production`.

### Pipeline Docker nao encontra arquivos

Confira as variaveis:

```text
DOCKER_CONTEXT=.
DOCKERFILE=src/MergeDuo.Copilot.Api/Dockerfile
```

Este projeto nao precisa do repositorio `MergeDuo.Aggregates` para buildar.

### Copilot Studio nao chama localhost

Use ngrok, Dev Tunnels ou deploy no ACA. O Copilot Studio precisa de uma URL HTTPS acessivel pela nuvem.

## Observacoes de producao

Esta API esta pronta para ACA, mas ainda e anonima. Antes de expor dados reais por longo periodo, considere:

- autenticar chamadas do Copilot Studio;
- colocar API Management na frente;
- restringir rede/origem;
- usar Managed Identity para Cosmos;
- remover `Development` da ACA;
- rotacionar chaves expostas;
- monitorar chamadas com Application Insights.
