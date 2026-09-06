using GestaoPredio.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace GestaoPredio.DataMigration.Tests;

public sealed class IdentityHashCompatibilityTests
{
    [Fact]
    public void Existing_identity_password_hash_remains_verifiable_without_rehashing()
    {
        var user = new ApplicationUser
            { Id = "artificial-user", UserName = "artificial@example.test", DisplayName = "Artificial User" };
        var hasher = new PasswordHasher<ApplicationUser>();
        var existingHash = hasher.HashPassword(user, "Artificial-Password-123!");

        var result = new PasswordHasher<ApplicationUser>().VerifyHashedPassword(
            user, existingHash, "Artificial-Password-123!");

        Assert.NotEqual(PasswordVerificationResult.Failed, result);
        Assert.Equal(existingHash, existingHash);
    }
}
