# Contexto de engenharia do projeto

Este arquivo é o mapa de contexto para agentes de IA que trabalharem em `PRINTERS_APP`. O workspace contém dois sistemas relacionados:

- `PRINTERS_APP`: API ASP.NET Core responsável principalmente por autenticação, emissão de JWT e consulta de permissões.
- `../ADI_WEB`: aplicação web Blazor Server que consome a API e fornece a interface operacional.

Leia este arquivo antes de alterar contratos entre a API e o web. O código-fonte continua sendo a fonte de verdade quando houver divergência.

## Objetivo e limites

`PRINTERS_APP` foi separado da API ADI original para funcionar sem dependência do ADI/OMEGA SQL Server. A persistência local usa SQLite e mantém o formato legado das tabelas de usuários e permissões para permitir migração de dados.

Não introduza módulos operacionais da aplicação web na API sem uma decisão explícita de arquitetura. Serviços, controllers e modelos novos devem permanecer na camada responsável pelo comportamento.

## Estrutura da API

```text
PRINTERS_APP/
  Program.cs                    composição da aplicação, DI, EF Core, JWT e pipeline
  Controllers/AuthController.cs endpoints de login e permissões
  Data/AppDbContext.cs           mapeamento EF Core para o schema legado
  Models/AuthModels.cs            LoginRequest, ApiResponse e PermissionResponse
  Models/User.cs                 usuário autenticável
  Models/UserPermission.cs       vínculo de usuário, módulo, permissão e nível
  Repositories/Repositories.cs   consultas de usuários e permissões
  Services/AuthService.cs        validação de senha e criação do resultado de login
  Services/TokenService.cs       emissão do JWT e suas claims
  Services/WindowsPrintSpooler.cs envio de bytes para a fila do spooler Windows
  Security/PasswordCipher.cs     compatibilidade com o hash Crypt legado
  appsettings*.json              SQLite, JWT e logging
```

### Runtime e dependências

- Target framework: `net9.0`.
- ASP.NET Core Web API com controllers.
- Entity Framework Core 9 com provider SQLite.
- JWT Bearer para autenticação da API.
- Swagger/Swashbuckle apenas no ambiente de desenvolvimento.
- Banco padrão: `Data Source=printers_app.db`.
- O schema é criado com `Database.EnsureCreated()` na inicialização.

### Pipeline

`Program.cs` registra controllers, Swagger, cache em memória, `AppDbContext`, autenticação JWT, autorização e os repositórios/serviços. A ordem relevante é:

1. `UseAuthentication()`.
2. `UseAuthorization()`.
3. `MapControllers()`.

O segredo JWT vem de `JwtSettings:Secret`. Nunca comite segredo real. O valor presente no arquivo de desenvolvimento deve ser substituído por configuração segura antes de publicar.

## Contrato da API

### `POST /api/Auth`

Recebe `LoginRequest` em JSON e retorna `ApiResponse<string>`. O campo `Value` contém o JWT quando o login é válido.

Campos relevantes do request:

- `Username`: login ou matrícula numérica.
- `Password`: senha em texto recebida pelo endpoint.
- `Platform`: `WEB`, `SWAGGER`, `SERVICES`, `API3` ou outra plataforma.
- `Environment`, `TargetOsType`, `Company`, `Branch`, `Establishment` e metadados opcionais.

Para as plataformas `WEB`, `SWAGGER`, `SERVICES` e `API3`, `AuthService` transforma a senha com `PasswordCipher.Crypt` antes de consultar `TB_USER`. Para outras plataformas, compara a senha recebida diretamente.

### `POST /api/Auth/auth-web`

Executa a mesma autenticação e, em caso de sucesso, grava o JWT na cookie HttpOnly `AuthTokenPRINTERS`. A cookie dura quatro horas e usa `SameSite=Strict`. Este endpoint é um contrato específico para clientes web; o `ADI_WEB` atual chama `POST /api/Auth` e grava a própria cookie de sessão.

### `GET /api/Auth/get-permission-user?module=1&permission=1`

Exige Bearer JWT. Lê a claim `IdAdi`, converte o módulo numérico para o nome legado e consulta `PERMISSAO_USER`. O mapa atual é:

| Código | Módulo |
| ---: | --- |
| 1 | `COMERCIAL` |
| 2 | `FINANCEIRO` |
| 3 | `QUALIDADE` |
| 4 | `ENGENHARIA` |
| 5 | `MANUFATURA` |
| 6 | `SUPRIMENTOS` |

Retorna uma lista de `PermissionResponse` com nível, módulo numérico, código da permissão e usuário. Ausência de registros resulta em status HTTP 404 dentro do envelope da resposta.

### Claims emitidas no JWT

`TokenService` emite `Matricula`, `IdAdi`, `Login`, `Name`, `Level`, `Environment`, `Plataforma`, `TargetOSType`, `Empresa`, `Filial` e `Estabelecimento`. A API valida assinatura e expiração, mas não valida issuer nem audience.

- `SERVICES` e `API3`: expiração em um ano.
- Demais plataformas: expiração em quatro horas.

## Persistência

O EF Core mapeia:

- `User` para `TB_USER`: `USERID`, `USERNOME`, `USERLOGIN`, `USERNIVEL`, `USERPASS`, `REGISTRO`.
- `UserPermission` para `PERMISSAO_USER`: `ID_REGISTRO`, `ID_USUARIO`, `ID_PERMISSAO`, `NIVEL`, `ID_COD`, `PERMNOME`, `PERMMODULO`.

Os repositórios usam `AsNoTracking()`. Usuários podem autenticar por login ou por matrícula numérica. A permissão considera `UserId`, código e módulo; `HasPermissionAsync` também considera nível mínimo.

### Retenção de impressão

As tabelas `DocumentosRetidos` e `ImpressorasConfig` são criadas pelo modelo EF Core. A inicialização também executa `CREATE TABLE IF NOT EXISTS` para que bancos SQLite existentes recebam as novas tabelas mesmo quando `EnsureCreated()` já havia sido executado anteriormente.

O usuário de teste `10840` é inserido se não existir, com senha armazenada como `PasswordCipher.Crypt("10840")`. O login continua sendo feito por `POST /api/Auth` e não altera o contrato de autenticação legado.

Endpoints adicionais:

- `POST /api/impressao/receber`: anônimo para o sistema de captura; `DadosArquivo` é um `byte[]` serializado como Base64 no JSON.
- `GET /api/impressao/pendentes`: autenticado; retorna apenas documentos `PENDENTE` associados às claims `Login`, `Matricula`, `IdAdi` ou identidade do JWT.
- `GET /api/impressao/pendentes/{id}`: autenticado; consulta um documento pendente do usuário.
- `POST /api/impressao/liberar`: autenticado; recebe `DocumentoId` e `ImpressoraDestino`, envia os bytes à fila Windows e só marca `IMPRESSO` depois do envio bem-sucedido.
- `GET /api/impressao/impressoras`: autenticado; lista impressoras ativas cadastradas.
- `POST /api/impressao/impressoras`: autenticado; cadastra nome da fila e IP físico, com unicidade em ambos.

O spooler usa `System.Printing` e, por isso, o projeto é `net9.0-windows` com `UseWPF`. O nome cadastrado em `NomeImpressora` precisa corresponder a uma fila instalada no Windows. O envio é bruto para a fila; o driver/fila deve aceitar o formato armazenado, inclusive PDF, ou o sistema de impressão precisa adicionar uma etapa de renderização específica.

## Estrutura do web

```text
../ADI_WEB/
  ADI_WEB/Program.cs                 DI, Blazor Server, HttpClient e autenticação
  ADI_WEB/Components/                App, rotas, layout e páginas Razor
  ADI_WEB/Security/                  cookie, handler JWT e usuário atual
  Domain/Helpers/BaseHttpService.cs  chamadas HTTP e Bearer token
  Domain/Helpers/Servidores.cs        seleção da URL base da API por ambiente
  Domain/Services/LoginService.cs     login e consulta de permissões
  Domain/Dtos/Login/                 DTO enviado no login
  Domain/Dtos/Permission/             DTO recebido para permissões
  Shared/Enums/                       enums compartilhados pelo web
```

O web é Blazor Server com componentes interativos e MudBlazor. O `HttpClient` principal usa `Servidores.GetBaseUrl()`, que já inclui o prefixo `/api/`. `BaseHttpService` lê o token da sessão (`DataSession.DadosToken.Token`) e envia `Authorization: Bearer <token>` em chamadas autenticadas.

A sessão do web usa a cookie `AuthTokenADI`, lida por `JwtCookieAuthenticationHandler`. Isso é diferente da cookie `AuthTokenPRINTERS` criada pelo endpoint `auth-web`. Não renomeie uma delas sem revisar o fluxo completo de autenticação.

## Integração atual e divergências conhecidas

Estas diferenças existem no código atual e devem ser verificadas antes de propor correções:

1. `Domain.Services.LoginService` chama `POST Auth`, não `POST Auth/auth-web`; portanto o web recebe o token no corpo e grava a sessão localmente.
2. O web monta `Auth/get-permission-user?modulo=...`, mas o controller da API declara o parâmetro `module`. Se a consulta falhar, este é o primeiro contrato a investigar.
3. O DTO web usa a propriedade `Plataforma`, enquanto `LoginRequest` da API usa `Platform`. Como o valor padrão do web é `WEB`, confirme o JSON efetivamente enviado antes de alterar os DTOs.
4. O DTO web chama a propriedade de estabelecimento `Estabelecineto`, enquanto a API espera `Establishment`. O web atual pode estar enviando zero nesse campo.
5. O DTO de resposta do web usa `Modulo`, enquanto a API retorna `Module`. A desserialização deve ser confirmada caso a tela de permissões receba valores padrão.
6. `Servidores` contém endereços de rede e escolhe um endereço diferente em `DEBUG` e `RELEASE`. Não hardcode uma nova URL em serviços individuais.
7. `ADI_WEB` ignora validação de certificado no `HttpClient` principal durante o desenvolvimento. Isso é conveniente para HTTPS interno, mas não deve ser levado como prática de produção.

## Regras para alterações

- Preserve os contratos JSON existentes, a menos que a alteração inclua atualização coordenada no web, na API e nos testes.
- Antes de mudar autenticação, trace o caminho completo: `LoginPage.razor` -> `LoginService` -> `BaseHttpService` -> `AuthController` -> `AuthService` -> `TokenService` -> cookie/claims.
- Antes de mudar permissões, confira simultaneamente o parâmetro da URL, o enum do web, o mapa de módulos da API, as colunas de `PERMISSAO_USER` e o nome da claim `IdAdi`.
- Não faça acesso direto ao SQLite a partir do web; use endpoints e serviços da API.
- Não copie a base `printers_app.db` para commits ou artefatos de publicação sem uma decisão explícita sobre dados.
- Para novos endpoints, use controller, DTO, serviço e repositório coerentes com o padrão existente e proteja endpoints privados com `[Authorize]`.
- Mantenha mensagens e documentação em português quando estiver trabalhando em superfícies já existentes em português.

## Verificação local

Na raiz de `PRINTERS_APP`:

```powershell
dotnet restore
dotnet build
dotnet run
```

Durante o desenvolvimento, Swagger fica disponível em `/swagger`. Para validar uma mudança de contrato, teste primeiro o login, depois use o JWT retornado no endpoint de permissões e confirme o JSON real enviado pelo `ADI_WEB`.
