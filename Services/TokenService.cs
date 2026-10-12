using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using PRINTERS_APP.Models;
using PRINTERS_APP.Security;

namespace PRINTERS_APP.Services;

public sealed class TokenService(IOptions<JwtSettings> settings)
{
    private readonly JwtSettings jwt = settings.Value;

    // Sobrecarga para o Login normal (Web / LoginRequest padrão)
    public string Create(User user, LoginRequest request, TimeSpan? expiresIn = null)
    {
        return GenerateToken(user, expiresIn ?? TimeSpan.FromSeconds(jwt.UserTokenSeconds), [
            new Claim("Matricula", user.Registration),
            new Claim("IdAdi", user.UserId.ToString()),
            new Claim("Login", user.Login ?? string.Empty),
            new Claim("UsernamePc", user.UsernamePc ?? string.Empty),
            new Claim("Name", user.Name ?? string.Empty),
            new Claim("Level", user.Level ?? string.Empty),
            new Claim("CanPrint", user.CanPrint ? "true" : "false"),
            new Claim("TokenType", "USER")
        ]);
    }

    // Login de equipamentos/serviços: validade definida por JwtSettings:DeviceTokenDays.
    public string Create(User user, LoginAppRequest request, TimeSpan? expiresIn = null)
    {
        return GenerateToken(user, expiresIn ?? TimeSpan.FromDays(jwt.DeviceTokenDays), [
            new Claim("Matricula", user.Registration),
            new Claim("IdAdi", user.UserId.ToString()),
            new Claim("Login", user.Login ?? string.Empty),
            new Claim("UsernamePc", user.UsernamePc ?? string.Empty),
            new Claim("Name", user.Name ?? string.Empty),
            new Claim("Level", user.Level ?? string.Empty),
            new Claim("CanPrint", user.CanPrint ? "true" : "false"),
            new Claim("CodeSession", request.CodeSession ?? string.Empty),
            new Claim("Service", request.Service ?? string.Empty),
            new Claim("Ipv4", request.Ipv4 ?? string.Empty),
            new Claim("TokenType", "SERVICE")
        ]);
    }

    // Método auxiliar privado para centralizar a criação do JWT
    private string GenerateToken(User user, TimeSpan expiresIn, Claim[] additionalClaims)
    {
        var now = DateTime.UtcNow;

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(additionalClaims),
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.Add(expiresIn),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
                SecurityAlgorithms.HmacSha256)
        };

        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }
}
