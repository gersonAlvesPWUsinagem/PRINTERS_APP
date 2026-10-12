# Publicação do PRINTERS_APP no IIS

Destino previsto: servidor `192.168.0.235`, que já hospeda outras APIs ASP.NET Core.

## Pré-requisitos

- IIS com o ASP.NET Core Module instalado pelo Hosting Bundle do .NET 9 x64.
- Um Application Pool exclusivo para esta API.
- Uma porta ou host name que não esteja em uso por outro binding.
- Uma conta de serviço de domínio com acesso de modificação à pasta do SQLite na rede.
- Conectividade TCP do servidor IIS até a impressora na porta 9100.

## Publicar

O perfil `FolderProfile` produz uma implantação `Release`, `win-x64`, autossuficiente
(`self-contained`) e com ambiente `Production`. Assim, a API não depende da instalação
do runtime .NET 9 no servidor:

```powershell
dotnet publish .\PRINTERS_APP.csproj -p:PublishProfile=FolderProfile --no-restore
```

Os arquivos são gerados em:

```text
bin\Release\net9.0-windows\win-x64\publish
```

Copie todo o conteúdo dessa pasta para uma pasta exclusiva no servidor, por exemplo:

```text
C:\Sites\PRINTERS_APP
```

Não copie `printers_app.db`, `printers_app.db-wal` ou `printers_app.db-shm` do
ambiente de desenvolvimento.

## Application Pool

Crie o pool `PRINTERS_APP` com estas opções:

- `.NET CLR Version`: `No Managed Code`.
- `Managed Pipeline Mode`: `Integrated`.
- `Enable 32-Bit Applications`: `False`.
- `Start Mode`: `AlwaysRunning`.
- `Identity`: conta de serviço de domínio dedicada.

A conta do pool precisa de leitura e execução em `C:\Sites\PRINTERS_APP` e de
modificação na pasta:

```text
\\servidor\Public\PPCP\Downloads\DATABASE
```

O acesso ao compartilhamento e o acesso NTFS devem permitir criar e alterar os
arquivos auxiliares `-wal` e `-shm` do SQLite.

## Site e binding

Crie um site ou aplicação IIS exclusivo usando o pool `PRINTERS_APP` e o caminho
físico `C:\Sites\PRINTERS_APP`. Antes de escolher a porta, confira todos os
bindings já existentes no IIS. Um exemplo, somente se a porta estiver livre, é:

```text
http://192.168.0.235:5182
```

Para produção definitiva, prefira um nome DNS e HTTPS, por exemplo:

```text
https://printers-api.dominio.local
```

## Configuração no IIS

O pacote funciona inicialmente com o segredo definido em `appsettings.json`, sem
configuração adicional no IIS. Quando for oportuno substituir o segredo, use o
`Configuration Editor` do site, em `system.webServer/aspNetCore`, e adicione em
`environmentVariables`:

```text
ASPNETCORE_ENVIRONMENT = Production
JwtSettings__Secret = <segredo aleatório com no mínimo 32 bytes, opcional>
Cors__AllowedOrigins__0 = http://192.168.0.235:<porta-do-PRINTERS_WEB>
```

Quando o web estiver em HTTPS, a origem também deve usar HTTPS. A origem não
deve terminar com `/`.

Se for necessário substituir a conexão sem alterar `appsettings.json`, configure:

```text
ConnectionStrings__Default = Data Source=\\servidor\Public\PPCP\Downloads\DATABASE\printers_app.db
```

Reinicie somente o Application Pool depois de alterar as variáveis.

## Validação

1. Abra `http://192.168.0.235:<porta>/api/impressao/pendentes`.
2. Uma resposta `401 Unauthorized` confirma que IIS, aplicação e rota estão ativos.
3. Teste o login por `POST /api/Auth` com um usuário real existente no SQLite.
4. Confirme que os logs do IIS não apresentam erro de acesso ao banco.
5. Do servidor, valide conectividade com a impressora:

```powershell
Test-NetConnection 192.168.0.13 -Port 9100
```

O Swagger não é exposto em `Production`.

## Atualizações posteriores

1. Pare o pool `PRINTERS_APP`.
2. Faça backup da pasta publicada e do banco.
3. Substitua os arquivos da aplicação, preservando as configurações do IIS.
4. Inicie o pool e repita a validação.

Nunca substitua o banco SQLite durante uma atualização da aplicação.
