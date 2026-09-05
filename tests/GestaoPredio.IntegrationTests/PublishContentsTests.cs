namespace GestaoPredio.IntegrationTests;

public sealed class PublishContentsTests
{
    [Fact]
    public void Web_publish_definition_includes_only_built_production_assets()
    {
        var root = FindRepositoryRoot();
        var project = File.ReadAllText(Path.Combine(root, "recepcaototem", "recepcaototem.csproj"));
        Assert.Contains("ClientApp/dist/**", project);
        Assert.Contains("wwwroot/%(ClientAppDist.RecursiveDir)", project);
        Assert.Contains("Exclude=\"$(MSBuildProjectDirectory)/ClientApp/dist/**/*.map\"", project);
        Assert.DoesNotContain("GestaoPredio.AdminCli", project);
        Assert.DoesNotContain("ClientApp/src/**", project);
        Assert.DoesNotContain("ClientApp/src/dev", project);
        Assert.DoesNotContain("ClientApp/src/data/mock", project);
        Assert.DoesNotContain("artifacts/sql", project);
        Assert.DoesNotContain("Storage__PrivateFilesPath", project);
        Assert.Contains("Content Remove=\"vercel.json\"", project);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "recepcaototem.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new DirectoryNotFoundException("Repository root not found.");
    }
}
