# MergeDuo.Copilot

Microservico .NET 8 anonimo para uso pelo Copilot Studio. Ele le dados financeiros
diretamente do Cosmos DB e nao chama APIs HTTP de outros microservicos.

## Configuracao obrigatoria

- `Copilot__UserId`
- `Copilot__BusinessTimeZone=America/Sao_Paulo`
- `Cosmos__Endpoint` ou `Cosmos__ConnectionString`
- `Cosmos__Database`
- `Cosmos__UsersContainer`
- `Cosmos__PartnershipsContainer`
- `Cosmos__MonthlyAggregatesContainer`
- `Cosmos__TransactionsContainer`
- `Cosmos__FixedRulesContainer`
- `Cosmos__CardsContainer`

## Endpoints

- `GET /copilot/month-summary/{year}/{month}`
- `GET /copilot/next-three-months?year=2026&month=5`
- `POST /copilot/purchase-simulation`
- `GET /healthz`
- `GET /readyz`

Os endpoints de `/copilot` nao exigem `Authorization` no v1. O escopo de dados vem do
`Copilot__UserId` configurado no ambiente; quando esse usuario tem merge ativo, as respostas
somam usuario e parceiro.

## Rodar localmente

```bash
dotnet run --project src/MergeDuo.Copilot.Api
```

## Testes

```bash
dotnet test MergeDuo.Copilot.sln
```

## Docker

O contexto do build deve ser `MergeDuo.Microservices/`, pois este projeto referencia
`MergeDuo.Aggregates.Domain`.

```bash
docker build -f MergeDuo.Copilot/src/MergeDuo.Copilot.Api/Dockerfile -t mergeduo-copilot:local .
```
