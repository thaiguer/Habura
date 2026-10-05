using Habura.Api.Models;

namespace Habura.Api.Services;

public interface IUserService
{
    Task<User?> ValidateCredentialsAsync(string username, string password);
    Task<User> CreateUserAsync(string username, string password);
}
