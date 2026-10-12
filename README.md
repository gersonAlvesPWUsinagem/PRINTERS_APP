# PRINTERS_APP

API de autenticação e permissões derivada da API ADI, sem os módulos operacionais e sem dependência de ADI/OMEGA SQL Server.

## Contexto para agentes de IA

O mapa de arquitetura, contratos entre `PRINTERS_APP` e `ADI_WEB`, fluxo de autenticação, persistência e divergências conhecidas está em [GEMINI.md](GEMINI.md). Consulte esse arquivo antes de alterar a integração entre a API e o web.

## Persistência

A API usa SQLite em `printers_app.db`. Na primeira execução, o schema é criado automaticamente com apenas:

- `TB_USER`: usuários e credenciais.
- `PERMISSAO_USER`: permissões por usuário, módulo, código e nível.

A aplicação não importa automaticamente dados do banco antigo. Para usar usuários existentes, copie os registros dessas duas tabelas para o SQLite respeitando os nomes das colunas.

## Executar

Copie `appsettings.example.json` para `appsettings.json` e configure a conexão e o segredo JWT localmente.

```powershell
dotnet restore
dotnet run
```

Swagger fica disponível em `/swagger` no ambiente de desenvolvimento.

## Endpoints

- `GET /` ou `GET /api/home`: informações públicas e estado da API.
- `POST /api/Auth`: retorna JWT.
- `POST /api/Auth/auth-web`: retorna JWT e grava cookie HttpOnly.
- `GET /api/Auth/get-permission-user?module=1&permission=1`: consulta permissões do usuário autenticado.

Para `WEB`, `SWAGGER`, `SERVICES` e `API3`, a senha usa o algoritmo legado `Crypt` da API de origem, mantendo compatibilidade com credenciais migradas.

O segredo JWT pode ser substituído em produção pela variável de ambiente
`JwtSettings__Secret`. Enquanto ela não for configurada, a aplicação utiliza o valor
presente em `appsettings.json`.

Consulte [PUBLICACAO-IIS.md](PUBLICACAO-IIS.md) para publicar no IIS.
