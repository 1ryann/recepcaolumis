namespace GestaoPredio.DataMigration;

public static class Program
{
    private const string PrepareConfirmation = "PREPARE_LUMIS_PRODUCTION_SCHEMA";
    private const string ExecuteConfirmation = "MIGRATE_GESTAOPREDIODB_TO_LUMIS_PRODUCTION";
    private const string ReplaceConfirmation = "REPLACE_LUMIS_PRODUCTION_PRELOAD_AFTER_IIS_STOP";

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0) return Usage();
            var settings = MigrationSettings.Load();
            using var cancellation = new CancellationTokenSource();
            Console.CancelKeyPress += (_, eventArgs) => { eventArgs.Cancel = true; cancellation.Cancel(); };
            return args[0] switch
            {
                "--fingerprint-target" => FingerprintTarget(settings),
                "--validate-source" => await ValidateSourceAsync(settings, cancellation.Token),
                "--validate-target" => await ValidateTargetAsync(settings, cancellation.Token),
                "--dry-run" => await DryRunAsync(settings, cancellation.Token),
                "--prepare-target-schema" => await PrepareTargetAsync(settings, args, cancellation.Token),
                "--execute" => await ExecuteAsync(settings, args, cancellation.Token),
                "--replace-import" => await ReplaceAsync(settings, args, cancellation.Token),
                "--reconcile" => await ReconcileAsync(settings, cancellation.Token),
                _ => Usage()
            };
        }
        catch (MigrationSafetyException exception)
        {
            Console.Error.WriteLine($"SAFETY STOP: {exception.Message}");
            return 2;
        }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine("Operation cancelled; no secret values were logged.");
            return 3;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"Migration failed safely ({exception.GetType().Name}). No connection values or row data were logged.");
            return 4;
        }
    }

    private static int FingerprintTarget(MigrationSettings settings)
    {
        var connection = settings.RequireTarget();
        var fingerprint = MigrationGuard.ComputeTargetFingerprint(connection);
        var identity = MigrationGuard.ValidateTarget(connection,
            settings.TargetProject ?? string.Empty, settings.TargetEnvironment ?? string.Empty, fingerprint);
        Console.WriteLine(identity.SafeDescription);
        Console.WriteLine($"CandidateFingerprint={identity.Fingerprint}");
        Console.WriteLine("Store this fingerprint externally as Migration:TargetFingerprint after confirming the project in Supabase.");
        return 0;
    }

    private static async Task<int> ValidateSourceAsync(MigrationSettings settings, CancellationToken ct)
    {
        var source = settings.RequireSource();
        var safe = MigrationGuard.ValidateSource(source);
        Console.WriteLine($"SOURCE: {safe.SafeDescription}");
        PrintInventory(await DatabaseInventoryReader.ReadSourceAsync(source, ct));
        return 0;
    }

    private static async Task<int> ValidateTargetAsync(MigrationSettings settings, CancellationToken ct)
    {
        var safe = settings.ValidateTarget();
        Console.WriteLine($"TARGET: {safe.SafeDescription}");
        PrintInventory(await DatabaseInventoryReader.ReadTargetAsync(settings.RequireTarget(), ct));
        return 0;
    }

    private static async Task<int> DryRunAsync(MigrationSettings settings, CancellationToken ct)
    {
        var sourceConnection = settings.RequireSource();
        var targetConnection = settings.RequireTarget();
        var source = MigrationGuard.ValidateSource(sourceConnection);
        var target = settings.ValidateTarget();
        Console.WriteLine($"SOURCE: {source.SafeDescription}");
        Console.WriteLine($"TARGET: {target.SafeDescription}");
        Console.WriteLine("MODE: DRY RUN (read-only)");
        PrintInventory(await DatabaseInventoryReader.ReadSourceAsync(sourceConnection, ct));
        PrintInventory(await DatabaseInventoryReader.ReadTargetAsync(targetConnection, ct));
        return 0;
    }

    private static async Task<int> PrepareTargetAsync(MigrationSettings settings, string[] args, CancellationToken ct)
    {
        RequireConfirmation(args, PrepareConfirmation);
        var target = settings.ValidateTarget();
        Console.WriteLine($"TARGET: {target.SafeDescription}");
        Console.WriteLine("MODE: APPLY APPROVED NPGSQL MIGRATIONS");
        await PostgreSqlSchemaPreparer.PrepareAsync(settings.RequireTarget(), ct);
        PrintInventory(await DatabaseInventoryReader.ReadTargetAsync(settings.RequireTarget(), ct));
        return 0;
    }

    private static async Task<int> ExecuteAsync(MigrationSettings settings, string[] args, CancellationToken ct)
    {
        RequireConfirmation(args, ExecuteConfirmation);
        var sourceConnection = settings.RequireSource();
        var targetConnection = settings.RequireTarget();
        var source = MigrationGuard.ValidateSource(sourceConnection);
        var target = settings.ValidateTarget();
        Console.WriteLine($"SOURCE: {source.SafeDescription}");
        Console.WriteLine($"TARGET: {target.SafeDescription}");
        Console.WriteLine("MODE: INITIAL TRANSACTIONAL IMPORT");
        var results = await DataMigrationRunner.ExecuteInitialLoadAsync(sourceConnection, targetConnection, ct);
        return await PrintReconciliationAsync(results, targetConnection, ct);
    }

    private static async Task<int> ReconcileAsync(MigrationSettings settings, CancellationToken ct)
    {
        var sourceConnection = settings.RequireSource();
        var targetConnection = settings.RequireTarget();
        var source = MigrationGuard.ValidateSource(sourceConnection);
        var target = settings.ValidateTarget();
        Console.WriteLine($"SOURCE: {source.SafeDescription}");
        Console.WriteLine($"TARGET: {target.SafeDescription}");
        Console.WriteLine("MODE: READ-ONLY RECONCILIATION");
        var results = await DataMigrationRunner.ReconcileAsync(sourceConnection, targetConnection, ct);
        return await PrintReconciliationAsync(results, targetConnection, ct);
    }

    private static async Task<int> ReplaceAsync(MigrationSettings settings, string[] args, CancellationToken ct)
    {
        RequireConfirmation(args, ReplaceConfirmation);
        var sourceConnection = settings.RequireSource();
        var targetConnection = settings.RequireTarget();
        var source = MigrationGuard.ValidateSource(sourceConnection);
        var target = settings.ValidateTarget();
        Console.WriteLine($"SOURCE: {source.SafeDescription}");
        Console.WriteLine($"TARGET: {target.SafeDescription}");
        Console.WriteLine("MODE: FINAL CONTROLLED REPLACEMENT; IIS MUST ALREADY BE STOPPED");
        var results = await DataMigrationRunner.ExecuteFinalReplacementAsync(sourceConnection, targetConnection, ct);
        return await PrintReconciliationAsync(results, targetConnection, ct);
    }

    private static async Task<int> PrintReconciliationAsync(IReadOnlyList<TableReconciliation> results,
        string targetConnection, CancellationToken ct)
    {
        foreach (var result in results)
            Console.WriteLine($"TABLE={result.Table}; SourceCount={result.SourceCount}; TargetCount={result.TargetCount}; Match={result.ContentMatches}");
        var foreignKeyErrors = await DataMigrationRunner.CountTargetForeignKeyErrorsAsync(targetConnection, ct);
        Console.WriteLine($"ForeignKeyErrors={foreignKeyErrors}");
        return results.All(x => x.ContentMatches) && foreignKeyErrors == 0 ? 0 : 5;
    }

    private static void PrintInventory(DatabaseInventory inventory)
    {
        Console.WriteLine($"Provider={inventory.Provider}; Database={inventory.Database}; MajorVersion={inventory.Version}; Tables={inventory.Tables.Count}");
        foreach (var table in inventory.Tables)
            Console.WriteLine($"TABLE={table}; Count={inventory.Counts[table]}");
        foreach (var migration in inventory.Migrations)
            Console.WriteLine($"MIGRATION={migration}");
    }

    private static void RequireConfirmation(string[] args, string expected)
    {
        var index = Array.IndexOf(args, "--confirm");
        if (index < 0 || index + 1 >= args.Length || !args[index + 1].Equals(expected, StringComparison.Ordinal))
            throw new MigrationSafetyException($"This command requires --confirm {expected}.");
    }

    private static int Usage()
    {
        Console.Error.WriteLine("Use --fingerprint-target, --validate-source, --validate-target, --dry-run, --prepare-target-schema, --execute, --replace-import, or --reconcile.");
        return 1;
    }
}
