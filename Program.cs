using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using PRINTERS_APP.Data;
using PRINTERS_APP.Repositories;
using PRINTERS_APP.Security;
using PRINTERS_APP.Services;
using PRINTERS_APP.Swagger;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// 1. Serviços Essenciais
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddCors(options =>
{
    options.AddPolicy("PrintersWeb", policy => policy
        .SetIsOriginAllowed(origin =>
        {
            if (string.Equals(origin, "null", StringComparison.OrdinalIgnoreCase))
                return true;

            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
                return false;

            return uri.IsLoopback
                || builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()?.Contains(
                    origin,
                    StringComparer.OrdinalIgnoreCase) == true;
        })
        .AllowCredentials()
        .AllowAnyHeader()
        .AllowAnyMethod());
});

// Configuração do Swagger com o suporte ao cadeado de autorização (Bearer)
builder.Services.AddSwaggerGen(options =>
{
    options.OperationFilter<CadastrarApiExampleOperationFilter>();
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Insira o token JWT no formato: Bearer {seu_token}"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// 2. Banco de Dados SQLite
var connectionString = builder.Configuration.GetConnectionString("Default") ?? "Data Source=printers_app.db";
builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));

// 3. JWT e Autenticação (emissão e validação usam a mesma seção)
var jwtSettings = builder.Configuration
    .GetRequiredSection(JwtSettings.SectionName)
    .Get<JwtSettings>()
    ?? throw new InvalidOperationException("A configuração JwtSettings não foi encontrada.");

if (string.IsNullOrWhiteSpace(jwtSettings.Issuer) || string.IsNullOrWhiteSpace(jwtSettings.Audience))
    throw new InvalidOperationException("JwtSettings:Issuer e JwtSettings:Audience são obrigatórios.");
if (Encoding.UTF8.GetByteCount(jwtSettings.Secret) < 32)
    throw new InvalidOperationException("JwtSettings:Secret deve possuir pelo menos 32 bytes.");

builder.Services.Configure<JwtSettings>(builder.Configuration.GetRequiredSection(JwtSettings.SectionName));
var jwtKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.Secret));

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = jwtKey,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateIssuer = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtSettings.Audience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            ClockSkew = TimeSpan.FromSeconds(jwtSettings.ClockSkewSeconds)
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (string.IsNullOrWhiteSpace(context.Token) &&
                    context.Request.Cookies.TryGetValue("AuthTokenPRINTERS", out var cookieToken))
                {
                    context.Token = cookieToken;
                }
                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                var authorization = context.HttpContext.Request.Headers.Authorization.ToString();
                var token = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                    ? authorization[7..].Trim()
                    : context.HttpContext.Request.Cookies["AuthTokenPRINTERS"];

                if (!string.IsNullOrWhiteSpace(token))
                {
                    var revocations = context.HttpContext.RequestServices
                        .GetRequiredService<TokenRevocationService>();
                    if (await revocations.IsRevokedAsync(token, context.HttpContext.RequestAborted))
                        context.Fail("Token revogado.");
                }
            }
        };
    });

// 4. Exige autenticação por padrão em todas as rotas (exceto as com [AllowAnonymous])
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("Impressao", policy => policy
        .RequireAuthenticatedUser()
        .RequireClaim("TokenType", "USER")
        .RequireClaim("CanPrint", "true"));

    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});

// 5. Repositórios e Serviços da Aplicação
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IPermissionRepository, PermissionRepository>();
builder.Services.AddScoped<IImpressaoRepository, ImpressaoRepository>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<TokenRevocationService>();
builder.Services.AddScoped<ApiLogService>();
builder.Services.AddSingleton<IPrintSpooler, WindowsPrintSpooler>();

var app = builder.Build();

// 6. Inicialização e Criação do Banco de Dados
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS PontosColeta (
            Id INTEGER NOT NULL CONSTRAINT PK_PontosColeta PRIMARY KEY AUTOINCREMENT,
            Nome TEXT NOT NULL,
            IpMaquina TEXT NOT NULL,
            ImpressoraId INTEGER NOT NULL,
            Ativo INTEGER NOT NULL DEFAULT 1,
            TokenValidadeDias INTEGER NOT NULL DEFAULT 360,
            CONSTRAINT FK_PontosColeta_ImpressorasConfig_ImpressoraId
                FOREIGN KEY (ImpressoraId) REFERENCES ImpressorasConfig (Id) ON DELETE RESTRICT
        );
        CREATE UNIQUE INDEX IF NOT EXISTS IX_PontosColeta_Nome ON PontosColeta (Nome);
        CREATE UNIQUE INDEX IF NOT EXISTS IX_PontosColeta_IpMaquina ON PontosColeta (IpMaquina);
        CREATE INDEX IF NOT EXISTS IX_PontosColeta_ImpressoraId ON PontosColeta (ImpressoraId);
        """);
    db.Database.ExecuteSqlRaw("""
        CREATE TABLE IF NOT EXISTS RevokedTokens (
            TokenHash TEXT NOT NULL CONSTRAINT PK_RevokedTokens PRIMARY KEY,
            ExpiresAtUtc TEXT NOT NULL,
            RevokedAtUtc TEXT NOT NULL
        );
        CREATE INDEX IF NOT EXISTS IX_RevokedTokens_ExpiresAtUtc ON RevokedTokens (ExpiresAtUtc);
        DELETE FROM RevokedTokens WHERE ExpiresAtUtc <= CURRENT_TIMESTAMP;
        """);

    var connection = db.Database.GetDbConnection();
    connection.Open();
    try
    {
        using var columnsCommand = connection.CreateCommand();
        columnsCommand.CommandText = "PRAGMA table_info('PontosColeta');";
        using var reader = columnsCommand.ExecuteReader();
        var possuiTokenValidadeDias = false;
        while (reader.Read())
            possuiTokenValidadeDias |= string.Equals(reader.GetString(1), "TokenValidadeDias", StringComparison.OrdinalIgnoreCase);

        reader.Close();
        if (!possuiTokenValidadeDias)
        {
            using var alterCommand = connection.CreateCommand();
            alterCommand.CommandText = "ALTER TABLE PontosColeta ADD COLUMN TokenValidadeDias INTEGER NOT NULL DEFAULT 360;";
            alterCommand.ExecuteNonQuery();
        }

        using var registroInfoCommand = connection.CreateCommand();
        registroInfoCommand.CommandText = "SELECT type FROM pragma_table_info('TB_USER') WHERE name = 'REGISTRO';";
        var tipoRegistro = Convert.ToString(registroInfoCommand.ExecuteScalar());
        if (!string.Equals(tipoRegistro, "TEXT", StringComparison.OrdinalIgnoreCase))
        {
            using var transaction = connection.BeginTransaction();
            using var migrateRegistroCommand = connection.CreateCommand();
            migrateRegistroCommand.Transaction = transaction;
            migrateRegistroCommand.CommandText = """
                ALTER TABLE TB_USER RENAME COLUMN REGISTRO TO REGISTRO_NUMERICO_LEGADO;
                ALTER TABLE TB_USER ADD COLUMN REGISTRO TEXT NOT NULL DEFAULT '';
                UPDATE TB_USER
                   SET REGISTRO = CASE
                       WHEN REGISTRO_NUMERICO_LEGADO IS NULL THEN ''
                       WHEN CAST(REGISTRO_NUMERICO_LEGADO AS REAL) = CAST(CAST(REGISTRO_NUMERICO_LEGADO AS REAL) AS INTEGER)
                           THEN printf('%.0f', CAST(REGISTRO_NUMERICO_LEGADO AS REAL))
                       ELSE CAST(REGISTRO_NUMERICO_LEGADO AS TEXT)
                   END;
                ALTER TABLE TB_USER DROP COLUMN REGISTRO_NUMERICO_LEGADO;
                """;
            migrateRegistroCommand.ExecuteNonQuery();
            transaction.Commit();
        }

        using var normalizeRegistroCommand = connection.CreateCommand();
        normalizeRegistroCommand.CommandText = """
            UPDATE TB_USER
               SET REGISTRO = substr(REGISTRO, 1, length(REGISTRO) - 2)
             WHERE substr(REGISTRO, -2) IN ('.0', ',0');
            """;
        normalizeRegistroCommand.ExecuteNonQuery();

        using var canPrintInfoCommand = connection.CreateCommand();
        canPrintInfoCommand.CommandText = "SELECT COUNT(*) FROM pragma_table_info('TB_USER') WHERE name = 'PODEIMPRIMIR';";
        var possuiPodeImprimir = Convert.ToInt32(canPrintInfoCommand.ExecuteScalar()) > 0;
        if (!possuiPodeImprimir)
        {
            using var addCanPrintCommand = connection.CreateCommand();
            addCanPrintCommand.CommandText = """
                ALTER TABLE TB_USER ADD COLUMN PODEIMPRIMIR INTEGER NOT NULL DEFAULT 0;
                UPDATE TB_USER SET PODEIMPRIMIR = 1, USERNIVEL = '0' WHERE USERNIVEL = '7';
                """;
            addCanPrintCommand.ExecuteNonQuery();
        }
    }
    finally
    {
        connection.Close();
    }

    const string loginAdministrador = "99999";
    var administrador = db.Users.FirstOrDefault(x => x.Login == loginAdministrador)
        ?? db.Users.FirstOrDefault(x => x.Login == "admin");
    if (administrador is null)
    {
        administrador = new PRINTERS_APP.Models.User
        {
            UserId = db.Users.Select(x => x.UserId).ToList().DefaultIfEmpty(0).Max() + 1
        };
        db.Users.Add(administrador);
    }

    administrador.Name = "ADMINISTRADOR";
    administrador.Login = loginAdministrador;
    administrador.Password = PasswordCipher.Crypt(loginAdministrador);
    administrador.Registration = "99999";
    administrador.Level = "6";
    db.SaveChanges();

    const string ipImpressoraRetirada = "192.168.0.115";
    var impressoraRetirada = db.ImpressorasConfig.FirstOrDefault(x => x.IpFisico == ipImpressoraRetirada);
    if (impressoraRetirada is null)
    {
        impressoraRetirada = new PRINTERS_APP.Models.ImpressoraConfig
        {
            NomeImpressora = "HP LaserJet E50145",
            IpFisico = ipImpressoraRetirada,
            Ativa = true
        };
        db.ImpressorasConfig.Add(impressoraRetirada);
        db.SaveChanges();
    }
    else
    {
        impressoraRetirada.Ativa = true;
    }

    const string ipPontoRetirada = "192.168.1.252";
    var pontoRetirada = db.PontosColeta.FirstOrDefault(x => x.IpMaquina == ipPontoRetirada);
    if (pontoRetirada is null)
    {
        pontoRetirada = new PRINTERS_APP.Models.PontoColeta
        {
            Nome = "NT000915",
            IpMaquina = ipPontoRetirada
        };
        db.PontosColeta.Add(pontoRetirada);
    }

    pontoRetirada.ImpressoraId = impressoraRetirada.Id;
    pontoRetirada.Ativo = true;
    db.SaveChanges();
}

// 7. Pipeline HTTP
app.UseSwagger();
app.UseSwaggerUI();

app.UseCors("PrintersWeb");
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();


//using Microsoft.AspNetCore.Authentication.JwtBearer;
//using Microsoft.Data.Sqlite;
//using Microsoft.EntityFrameworkCore;
//using Microsoft.IdentityModel.Tokens;
//using Microsoft.OpenApi.Models;
//using PRINTERS_APP.Data;
//using PRINTERS_APP.Repositories;
//using PRINTERS_APP.Security;
//using PRINTERS_APP.Services;
//using System.Text;

//var builder = WebApplication.CreateBuilder(args);

//builder.Services.AddControllers();
//builder.Services.AddEndpointsApiExplorer();
//builder.Services.AddCors(options =>
//{
//    options.AddPolicy("PrintersWeb", policy => policy
//        .SetIsOriginAllowed(origin =>
//        {
//            // Permite temporariamente o PRINTERS_WEB aberto diretamente por file://.
//            // Nesse modo o navegador envia a origem literal "null" e o web usa Bearer.
//            if (string.Equals(origin, "null", StringComparison.OrdinalIgnoreCase))
//                return true;

//            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
//                return false;

//            return uri.IsLoopback
//                || builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()?.Contains(
//                    origin,
//                    StringComparer.OrdinalIgnoreCase) == true;
//        })
//        .AllowCredentials()
//        .AllowAnyHeader()
//        .AllowAnyMethod());
//});
//builder.Services.AddSwaggerGen(options =>
//{
//    options.OperationFilter<LoginSwaggerOperationFilter>();
//    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
//    {
//        Name = "Authorization",
//        Type = SecuritySchemeType.Http,
//        Scheme = "bearer",
//        BearerFormat = "JWT",
//        In = ParameterLocation.Header,
//        Description = "Informe o token JWT recebido no login."
//    });
//    options.AddSecurityRequirement(new OpenApiSecurityRequirement
//    {
//        {
//            new OpenApiSecurityScheme
//            {
//                Reference = new OpenApiReference
//                {
//                    Type = ReferenceType.SecurityScheme,
//                    Id = "Bearer"
//                }
//            },
//            Array.Empty<string>()
//        }
//    });
//});
//builder.Services.AddMemoryCache();

//var configuredConnectionString = builder.Configuration.GetConnectionString("Default") ?? "Data Source=printers_app.db";
//var sqliteConnectionString = new SqliteConnectionStringBuilder(configuredConnectionString)
//{
//    // Evita que conexões físicas ociosas mantenham o arquivo aberto após o uso.
//    Pooling = false
//}.ToString();
//builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlite(sqliteConnectionString));

//var jwtSecret = builder.Configuration["JwtSettings:Secret"]
//    ?? throw new InvalidOperationException("JwtSettings:Secret não foi configurado.");
//if (Encoding.UTF8.GetByteCount(jwtSecret) < 32)
//    throw new InvalidOperationException("JwtSettings:Secret deve possuir pelo menos 32 bytes.");

//var jwtKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret));

//builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
//    .AddJwtBearer(options =>
//    {
//        options.RequireHttpsMetadata = false;
//        options.SaveToken = true;
//        options.TokenValidationParameters = new TokenValidationParameters
//        {
//            ValidateIssuerSigningKey = true,
//            IssuerSigningKey = jwtKey,
//            ValidateIssuer = false,
//            ValidateAudience = false,
//            ValidateLifetime = true,
//            ClockSkew = TimeSpan.Zero
//        };
//        options.Events = new JwtBearerEvents
//        {
//            OnMessageReceived = context =>
//            {
//                if (string.IsNullOrWhiteSpace(context.Token)
//                    && context.Request.Cookies.TryGetValue("AuthTokenPRINTERS", out var cookieToken))
//                {
//                    context.Token = cookieToken;
//                }

//                return Task.CompletedTask;
//            }
//        };
//    });
//builder.Services.AddAuthorization();
//builder.Services.AddScoped<IUserRepository, UserRepository>();
//builder.Services.AddScoped<IPermissionRepository, PermissionRepository>();
//builder.Services.AddScoped<IImpressaoRepository, ImpressaoRepository>();
//builder.Services.AddScoped<AuthService>();
//builder.Services.AddScoped<TokenService>();
//builder.Services.AddScoped<ApiLogService>();
//builder.Services.AddSingleton<IPrintSpooler, WindowsPrintSpooler>();

//var app = builder.Build();

//// Navegadores Chromium enviam este preflight quando um front aberto por file://
//// acessa uma API HTTP na rede privada. Mantido enquanto o web for distribuído localmente.
//app.Use(async (context, next) =>
//{
//    if (string.Equals(
//        context.Request.Headers["Access-Control-Request-Private-Network"],
//        "true",
//        StringComparison.OrdinalIgnoreCase))
//    {
//        context.Response.Headers["Access-Control-Allow-Private-Network"] = "true";
//    }

//    await next();
//});

//using (var scope = app.Services.CreateScope())
//{
//    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
//    db.Database.OpenConnection();

//    // Mantém as transações no arquivo principal. WAL não é apropriado para o banco
//    // em compartilhamento SMB e cria os auxiliares -wal/-shm vistos no servidor.
//    try
//    {
//        using var journalModeCommand = db.Database.GetDbConnection().CreateCommand();
//        journalModeCommand.CommandText = "PRAGMA journal_mode=DELETE;";
//        var journalMode = Convert.ToString(journalModeCommand.ExecuteScalar());
//        if (!string.Equals(journalMode, "delete", StringComparison.OrdinalIgnoreCase))
//        {
//            app.Logger.LogWarning(
//                "O SQLite permaneceu em journal_mode={JournalMode}. Feche outros acessos ao banco e reinicie a API para tentar novamente.",
//                journalMode ?? "desconhecido");
//        }
//    }
//    catch (SqliteException exception) when (exception.SqliteErrorCode is 5 or 6)
//    {
//        // Um DB Browser ou uma instância anterior pode manter um bloqueio durante a
//        // troca de modo. Isso não deve impedir toda a API de iniciar.
//        app.Logger.LogWarning(exception,
//            "O banco está ocupado e o journal_mode não pôde ser alterado nesta inicialização.");
//    }

//    db.Database.ExecuteSqlRaw("PRAGMA synchronous=FULL;");
//    db.Database.EnsureCreated();
//    db.Database.ExecuteSqlRaw("""
//        CREATE TABLE IF NOT EXISTS DocumentosRetidos (
//            Id INTEGER NOT NULL CONSTRAINT PK_DocumentosRetidos PRIMARY KEY AUTOINCREMENT,
//            Username TEXT NOT NULL,
//            NomeArquivo TEXT NOT NULL,
//            DadosArquivo BLOB NOT NULL,
//            DataEnvio TEXT NOT NULL,
//            Status TEXT NOT NULL DEFAULT 'PENDENTE',
//            ImpressoraDestino TEXT NULL
//        );
//        CREATE INDEX IF NOT EXISTS IX_DocumentosRetidos_Username_Status
//            ON DocumentosRetidos (Username, Status);
//        CREATE TABLE IF NOT EXISTS ImpressorasConfig (
//            Id INTEGER NOT NULL CONSTRAINT PK_ImpressorasConfig PRIMARY KEY AUTOINCREMENT,
//            NomeImpressora TEXT NOT NULL,
//            IpFisico TEXT NOT NULL,
//            Ativa INTEGER NOT NULL DEFAULT 1
//        );
//        CREATE UNIQUE INDEX IF NOT EXISTS IX_ImpressorasConfig_NomeImpressora
//            ON ImpressorasConfig (NomeImpressora);
//        CREATE UNIQUE INDEX IF NOT EXISTS IX_ImpressorasConfig_IpFisico
//            ON ImpressorasConfig (IpFisico);
//        CREATE TABLE IF NOT EXISTS ApiLogs (
//            Id INTEGER NOT NULL CONSTRAINT PK_ApiLogs PRIMARY KEY AUTOINCREMENT,
//            DataHora TEXT NOT NULL,
//            Nivel TEXT NOT NULL,
//            Evento TEXT NOT NULL,
//            Mensagem TEXT NOT NULL,
//            Endpoint TEXT NULL,
//            Usuario TEXT NULL,
//            Detalhes TEXT NULL
//        );
//        CREATE INDEX IF NOT EXISTS IX_ApiLogs_DataHora ON ApiLogs (DataHora);
//        CREATE INDEX IF NOT EXISTS IX_ApiLogs_Evento ON ApiLogs (Evento);
//        """);

//    foreach (var printerSection in builder.Configuration.GetSection("Printing:Printers").GetChildren())
//    {
//        var name = printerSection["Name"]?.Trim();
//        var address = printerSection["Address"]?.Trim();
//        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(address))
//            continue;

//        var configuredPrinter = db.ImpressorasConfig.FirstOrDefault(x => x.NomeImpressora == name);
//        if (configuredPrinter is null)
//        {
//            db.ImpressorasConfig.Add(new PRINTERS_APP.Models.ImpressoraConfig
//            {
//                NomeImpressora = name,
//                IpFisico = address,
//                Ativa = true
//            });
//        }
//        else
//        {
//            configuredPrinter.IpFisico = address;
//            configuredPrinter.Ativa = true;
//        }
//    }
//    try
//    {
//        db.SaveChanges();
//    }
//    catch (DbUpdateException exception)
//    {
//        // Um cadastro manual preexistente pode possuir o mesmo nome ou IP. A API
//        // continua disponível para que o conflito seja corrigido pelo painel.
//        app.Logger.LogWarning(exception, "Não foi possível sincronizar as impressoras configuradas no appsettings.");
//        db.ChangeTracker.Clear();
//    }

//    var connection = db.Database.GetDbConnection();
//    if (connection.State != System.Data.ConnectionState.Open)
//        connection.Open();
//    using (var checkColumn = connection.CreateCommand())
//    {
//        checkColumn.CommandText = "SELECT COUNT(*) FROM pragma_table_info('DocumentosRetidos') WHERE name = 'NumeroPaginas';";
//        var columnExists = Convert.ToInt32(checkColumn.ExecuteScalar()) > 0;
//        if (!columnExists)
//        {
//            using var addColumn = connection.CreateCommand();
//            addColumn.CommandText = "ALTER TABLE DocumentosRetidos ADD COLUMN NumeroPaginas INTEGER NOT NULL DEFAULT 1;";
//            addColumn.ExecuteNonQuery();
//        }
//    }

//    using (var checkUsernamePc = connection.CreateCommand())
//    {
//        checkUsernamePc.CommandText = "SELECT COUNT(*) FROM pragma_table_info('TB_USER') WHERE name = 'USERNAMEPC';";
//        var columnExists = Convert.ToInt32(checkUsernamePc.ExecuteScalar()) > 0;
//        if (!columnExists)
//        {
//            using var addColumn = connection.CreateCommand();
//            addColumn.CommandText = "ALTER TABLE TB_USER ADD COLUMN USERNAMEPC TEXT NULL;";
//            addColumn.ExecuteNonQuery();
//        }
//    }

//    using (var createUsernamePcIndex = connection.CreateCommand())
//    {
//        createUsernamePcIndex.CommandText = "CREATE UNIQUE INDEX IF NOT EXISTS IX_TB_USER_USERNAMEPC ON TB_USER (USERNAMEPC);";
//        createUsernamePcIndex.ExecuteNonQuery();
//    }

//}

//app.Use(async (context, next) =>
//{
//    var inicio = DateTime.UtcNow;
//    try
//    {
//        await next();
//        if (!context.Request.Path.StartsWithSegments("/api/diagnostico/logs"))
//        {
//            await using var scope = app.Services.CreateAsyncScope();
//            var logger = scope.ServiceProvider.GetRequiredService<ApiLogService>();
//            await logger.RegistrarAsync(
//                context.Response.StatusCode >= 400 ? "AVISO" : "INFO",
//                "REQUISICAO_HTTP",
//                $"{context.Request.Method} {context.Request.Path} respondeu HTTP {context.Response.StatusCode} em {(DateTime.UtcNow - inicio).TotalMilliseconds:F0} ms.",
//                context.Request.Path,
//                context.User.FindFirst("Login")?.Value);
//        }
//    }
//    catch (Exception ex)
//    {
//        try
//        {
//            await using var scope = app.Services.CreateAsyncScope();
//            var logger = scope.ServiceProvider.GetRequiredService<ApiLogService>();
//            await logger.RegistrarAsync(
//                "ERRO", "EXCECAO_NAO_TRATADA", ex.Message,
//                context.Request.Path, context.User.FindFirst("Login")?.Value, ex.ToString());
//        }
//        catch { }
//        throw;
//    }
//});

//app.UseCors("PrintersWeb");

//app.UseSwagger();
//app.UseSwaggerUI();

//app.UseAuthentication();
//app.UseAuthorization();

//app.MapControllers();

//app.Run();
