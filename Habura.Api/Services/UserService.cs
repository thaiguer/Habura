using Habura.Api.Data;
using Habura.Api.Models;
using Habura.Api.Security;
using Microsoft.EntityFrameworkCore;

namespace Habura.Api.Services;

public class UserService : IUserService
{
    private readonly AppDbContext _db;

    public UserService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<User> CreateUserAsync(string username, string password)
    {
        if (await _db.Users.AnyAsync(u => u.Username == username))
            throw new InvalidOperationException("User already exists");

        var (hash, salt) = PasswordHasher.CreateHash(password);

        var user = new User
        {
            Username = username,
            PasswordHash = hash,
            PasswordSalt = salt
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    public async Task<User?> ValidateCredentialsAsync(string username, string password)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Username == username);
        if (user is null) return null;

        if (PasswordHasher.VerifyHash(password, user.PasswordHash, user.PasswordSalt))
            return user;

        return null;
    }
}
