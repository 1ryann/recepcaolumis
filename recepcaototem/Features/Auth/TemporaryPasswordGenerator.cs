using System.Security.Cryptography;

namespace recepcaototem.Features.Auth;

public interface ITemporaryPasswordGenerator
{
    string Generate();
}

/// <summary>
/// Builds the one-time password handed to a new account, or to one whose password an administrator
/// reset. It used to be twenty random characters with symbols, which reception had to read out over
/// the phone character by character; the shape is now two ordinary words and three digits
/// ("janela-cravo-482"), easy to dictate and to type on a phone keyboard.
/// <para>
/// That trades entropy for legibility on purpose: about 23 bits, against a password that is valid
/// for a single sign-in (<c>MustChangePassword</c> forces the change) and sits behind the login rate
/// limiter and a five-attempt lockout, which put online guessing out of reach. It is never a
/// long-lived secret. Every result satisfies <c>LumisIdentityOptions</c>: at least six characters,
/// a lowercase letter and a digit.
/// </para>
/// </summary>
public sealed class TemporaryPasswordGenerator : ITemporaryPasswordGenerator
{
    // Accent-free, unambiguous and neutral, so nothing is lost when the word is spoken, typed
    // without a Brazilian keyboard layout, or paired at random with another one.
    private static readonly string[] Words =
    [
        "areia", "banco", "barco", "bolso", "bonde", "brisa", "campo", "canoa",
        "carro", "carta", "cedro", "cesto", "chave", "cinema", "circo", "cobre",
        "corda", "costa", "cravo", "disco", "duna", "ferro", "festa", "folha",
        "forno", "fruta", "galho", "garra", "gelo", "grade", "grama", "janela",
        "jarra", "lago", "leite", "linha", "livro", "lona", "luva", "malha",
        "manga", "mapa", "mesa", "moeda", "monte", "nuvem", "oceano", "onda",
        "palco", "panela", "papel", "parque", "pedra", "pente", "pinho", "pipa",
        "planta", "porta", "praia", "prata", "quadro", "queijo", "raiz", "ramo",
        "rede", "relva", "rio", "roda", "rosa", "sala", "selo", "serra",
        "sino", "soja", "tapete", "teia", "telha", "tempo", "tinta", "torre",
        "trigo", "tropa", "tucano", "uva", "vaso", "vela", "vento", "verde",
        "vidro", "vinho", "violeta", "zebra"
    ];

    public string Generate()
    {
        var first = RandomNumberGenerator.GetInt32(Words.Length);
        int second;
        // A repeated word ("vento-vento-482") reads like a mistake and is one word of entropy short.
        do { second = RandomNumberGenerator.GetInt32(Words.Length); } while (second == first);
        var digits = RandomNumberGenerator.GetInt32(100, 1000);
        return $"{Words[first]}-{Words[second]}-{digits}";
    }
}
