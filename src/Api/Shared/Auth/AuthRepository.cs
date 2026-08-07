using Microsoft.EntityFrameworkCore;
using Api.Shared.Persistence;

namespace Api.Shared.Auth;

public class AuthRepository(PlaydexDbContext db)
{
    public virtual Task<User?> FindByEmail(string email, CancellationToken cancellationToken = default) =>
        db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Email == email, cancellationToken);

    public virtual Task<User?> FindById(long userId, CancellationToken cancellationToken = default) =>
        db.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == userId, cancellationToken);
}
