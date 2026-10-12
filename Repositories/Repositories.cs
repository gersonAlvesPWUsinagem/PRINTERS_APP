using Microsoft.EntityFrameworkCore;
using PRINTERS_APP.Data;
using PRINTERS_APP.Models;

namespace PRINTERS_APP.Repositories;

public interface IUserRepository
{
    Task<User?> FindAsync(string login, IEnumerable<string> passwords, CancellationToken cancellationToken);
}

public sealed class UserRepository(AppDbContext db) : IUserRepository
{
    public Task<User?> FindAsync(string login, IEnumerable<string> passwords, CancellationToken cancellationToken)
    {
        login = login.Trim();
        var acceptedPasswords = passwords.Distinct().ToArray();
        return db.Users.AsNoTracking().FirstOrDefaultAsync(
            x => x.Login == login && x.Password != null && acceptedPasswords.Contains(x.Password),
            cancellationToken);
    }
}

public interface IPermissionRepository
{
    Task<bool> HasPermissionAsync(int userId, int permission, string module, int level, CancellationToken cancellationToken);
    Task<List<UserPermission>> FindAsync(int userId, int permission, string module, CancellationToken cancellationToken);
}

public sealed class PermissionRepository(AppDbContext db) : IPermissionRepository
{
    public Task<bool> HasPermissionAsync(int userId, int permission, string module, int level, CancellationToken cancellationToken) =>
        db.UserPermissions.AsNoTracking().AnyAsync(x => x.UserId == userId && x.Code == permission && x.Module == module && x.Level >= level, cancellationToken);

    public Task<List<UserPermission>> FindAsync(int userId, int permission, string module, CancellationToken cancellationToken) =>
        db.UserPermissions.AsNoTracking().Where(x => x.UserId == userId && x.Code == permission && x.Module == module).ToListAsync(cancellationToken);
}
