using System.Text;

namespace GestaoPredio.AdminCli;

public interface ISecretConsole
{
    ConsoleKeyInfo ReadKey(bool intercept);
    void WriteLine();
}

public sealed class SystemSecretConsole : ISecretConsole
{
    public ConsoleKeyInfo ReadKey(bool intercept) => Console.ReadKey(intercept);
    public void WriteLine() => Console.WriteLine();
}

public sealed class HiddenPasswordReader(ISecretConsole console)
{
    public string Read()
    {
        var value = new StringBuilder();
        while (true)
        {
            var key = console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                console.WriteLine();
                return value.ToString();
            }
            if (key.Key == ConsoleKey.Backspace)
            {
                if (value.Length > 0) value.Length--;
                continue;
            }
            if (!char.IsControl(key.KeyChar)) value.Append(key.KeyChar);
        }
    }
}
