using System.Security.Cryptography;

namespace recepcaototem.Features.Auth;

public interface ITemporaryPasswordGenerator
{
    string Generate();
}

public sealed class TemporaryPasswordGenerator : ITemporaryPasswordGenerator
{
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!@$%*-_";
    private const string All = Upper + Lower + Digits + Symbols;

    public string Generate()
    {
        Span<char> password = stackalloc char[20];
        password[0] = Pick(Upper);
        password[1] = Pick(Lower);
        password[2] = Pick(Digits);
        password[3] = Pick(Symbols);
        for (var index = 4; index < password.Length; index++) password[index] = Pick(All);
        for (var index = password.Length - 1; index > 0; index--)
        {
            var swap = RandomNumberGenerator.GetInt32(index + 1);
            (password[index], password[swap]) = (password[swap], password[index]);
        }
        return new string(password);
    }

    private static char Pick(string source) => source[RandomNumberGenerator.GetInt32(source.Length)];
}
