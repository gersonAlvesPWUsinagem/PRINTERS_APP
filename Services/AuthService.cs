using PRINTERS_APP.Helpers;
using PRINTERS_APP.Models;
using PRINTERS_APP.Repositories;
using PRINTERS_APP.Security;
using PRINTERS_APP.Data;
using Microsoft.EntityFrameworkCore;

namespace PRINTERS_APP.Services;

public interface IAuthService
{
    Task<ApiDataService<string>> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<ApiDataService<LoginAppResponse>> LoginAsync(LoginAppRequest request, CancellationToken cancellationToken);
}

public sealed class AuthService(IUserRepository users, TokenService tokens, AppDbContext db) : IAuthService
{
    // 1. Login Web / Padrão (Usa o contrato original do IUserRepository)
    public async Task<ApiDataService<string>> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var passwords = PasswordsCompativeis(request.Password!);

        var user = await users.FindAsync(request.Login!, passwords, cancellationToken);
        if (user is null)
            return new ApiDataService<string> { Success = false, Message = "Usuário ou senha incorretos.", StatusCode = 401 };

        if (!user.CanPrint)
            return new ApiDataService<string>
            {
                Success = false,
                Message = "Usuário sem permissão de impressão.",
                StatusCode = 403
            };

        var token = tokens.Create(user, request);
        return new ApiDataService<string> { Success = true, Message = "Login realizado com sucesso", StatusCode = 200, Value = token };
    }

    // 2. Login de Equipamento/App (LoginAppRequest)
    public async Task<ApiDataService<LoginAppResponse>> LoginAsync(LoginAppRequest request, CancellationToken cancellationToken)
    {
        var passwords = PasswordsCompativeis(request.Password!);

        // Nota: Se o equipamento envia o IP no campo de identificação ou se existe um usuário padrão para o serviço,
        // ajustamos aqui. Como o IUserRepository busca por identifier (Login ou Matrícula), 
        // caso o equipamento envie o login/matrícula no campo de senha ou precise de uma regra por IP, 
        // podemos validar a senha. Se você busca por IP na tabela de usuários, me avise.
        // Por enquanto, vamos validar a senha e buscar por um identificador genérico ou pelo Service se necessário:

        var user = await users.FindAsync(request.Service, passwords, cancellationToken);
        if (user is null)
            return new ApiDataService<LoginAppResponse> { Success = false, Message = "Credenciais do equipamento incorretas.", StatusCode = 401 };

        var validadeDias = await db.PontosColeta.AsNoTracking()
            .Where(x => x.Ativo && x.IpMaquina == request.Ipv4)
            .Select(x => (int?)x.TokenValidadeDias)
            .FirstOrDefaultAsync(cancellationToken);

        var token = tokens.Create(
            user,
            request,
            validadeDias.HasValue ? TimeSpan.FromDays(validadeDias.Value) : null);

        // Retorna exatamente o objeto solicitado pelo equipamento
        var responseData = new LoginAppResponse(
            token,
            request.Service,
            request.Ipv4,
            request.TargetOSType,
            request.CodeSession ?? string.Empty);

        return new ApiDataService<LoginAppResponse>
        {
            Success = true,
            Message = "Login do equipamento realizado com sucesso",
            StatusCode = 200,
            Value = responseData
        };
    }

    private static string[] PasswordsCompativeis(string password) =>
    [
        PasswordCipher.Crypt(password),
        PasswordCipher.EncryptLegacy(password)
    ];
}
