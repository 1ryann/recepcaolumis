using System.Text.RegularExpressions;
using GestaoPredio.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using recepcaototem.Features.Auth;

namespace GestaoPredio.UnitTests;

public class TemporaryPasswordGeneratorTests
{
    private static readonly TemporaryPasswordGenerator Generator = new();
    private static readonly Regex Shape = new("^[a-z]{2,8}-[a-z]{2,8}-[1-9][0-9]{2}$", RegexOptions.Compiled);

    [Fact]
    public void Reads_as_two_words_and_three_digits()
    {
        foreach (var password in Enumerable.Range(0, 200).Select(_ => Generator.Generate()))
        {
            Assert.Matches(Shape, password);
            var parts = password.Split('-');
            Assert.NotEqual(parts[0], parts[1]);
        }
    }

    [Fact]
    public void Satisfies_the_password_policy_of_the_application()
    {
        var options = new IdentityOptions();
        LumisIdentityOptions.Configure(options);
        foreach (var password in Enumerable.Range(0, 200).Select(_ => Generator.Generate()))
        {
            Assert.True(password.Length >= options.Password.RequiredLength);
            Assert.Contains(password, char.IsAsciiDigit);
            Assert.Contains(password, char.IsAsciiLetterLower);
        }
    }

    [Fact]
    public void Does_not_repeat_itself()
    {
        // Legibility cost entropy on purpose (see TemporaryPasswordGenerator), but two accounts
        // created in a row must not end up sharing a password.
        var generated = Enumerable.Range(0, 200).Select(_ => Generator.Generate()).ToHashSet();
        Assert.True(generated.Count > 190, $"Only {generated.Count} distinct passwords out of 200.");
    }
}
