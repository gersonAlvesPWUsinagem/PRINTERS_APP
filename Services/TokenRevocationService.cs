using System.IdentityModel.Tokens.Jwt;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PRINTERS_APP.Data;
using PRINTERS_APP.Models;

namespace PRINTERS_APP.Services;

public sealed class TokenRevocationService(AppDbContext db)
{
    public async Task RevokeAsync(string token, CancellationToken cancellationToken = default)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        var hash = Hash(token);
        var registro = await db.RevokedTokens.FindAsync([hash], cancellationToken);

        if (registro is null)
        {
            db.RevokedTokens.Add(new RevokedToken
            {
                TokenHash = hash,
                ExpiresAtUtc = jwt.ValidTo,
                RevokedAtUtc = DateTime.UtcNow
            });
        }
        else
        {
            registro.ExpiresAtUtc = jwt.ValidTo;
            registro.RevokedAtUtc = DateTime.UtcNow;
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<bool> IsRevokedAsync(string token, CancellationToken cancellationToken = default)
    {
        var hash = Hash(token);
        var now = DateTime.UtcNow;
        return db.RevokedTokens.AsNoTracking()
            .AnyAsync(x => x.TokenHash == hash && x.ExpiresAtUtc > now, cancellationToken);
    }

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
