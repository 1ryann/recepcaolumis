namespace GestaoPredio.AdminCli;

public static class BootstrapCommandParser
{
    public static bool IsBootstrapAdmin(string[] args) =>
        args.Length == 1 && string.Equals(args[0], "bootstrap-admin", StringComparison.Ordinal);
}
