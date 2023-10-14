var target = Argument("Target", "Default");
var configuration =
    HasArgument("Configuration") ? Argument<string>("Configuration") :
    EnvironmentVariable("Configuration", "Release");

var artefactsDirectory = Directory("./Artefacts");

Task("Version")
    .Does(() =>
    {
        Information("Versioning software for configuration {0}...", configuration);
        GitVersion(new GitVersionSettings
        {
            UpdateAssemblyInfo = true,
            OutputType = GitVersionOutput.BuildServer
        });
        GitVersion versionInfo = GitVersion(new GitVersionSettings { OutputType = GitVersionOutput.Json });

        Information("Semantic Version: " + versionInfo.AssemblySemVer);
        Information("Full Semantic Version: " + versionInfo.AssemblySemFileVer);
        Information("Informational Version: " + versionInfo.InformationalVersion);
    });

Task("Clean")
    .Description("Cleans the artefacts, bin and obj directories.")
    .Does(() =>
    {
        CleanDirectory(artefactsDirectory);
        DeleteDirectories(GetDirectories("**/bin"), new DeleteDirectorySettings() { Force = true, Recursive = true });
        DeleteDirectories(GetDirectories("**/obj"), new DeleteDirectorySettings() { Force = true, Recursive = true });
    });

Task("Restore")
    .Description("Restores NuGet packages.")
    .IsDependentOn("Clean")
    .Does(() =>
    {
        DotNetRestore();
    });

Task("Build")
    .Description("Builds the solution.")
    .IsDependentOn("Restore")
    .Does(() =>
    {
        DotNetBuild(
            ".",
            new DotNetBuildSettings()
            {
                Configuration = configuration,
                NoRestore = true,
            });
    });

Task("Test")
    .Description("Runs unit tests and outputs test results to the artefacts directory.")
    .DoesForEach(
        GetFiles("./Tests/**/*.csproj"),
        project =>
        {
            DotNetTest(
                project.ToString(),
                new DotNetTestSettings()
                {
                    Blame = true,
                    Collectors = new string[] { "Code Coverage", "XPlat Code Coverage" },
                    Configuration = configuration,
                    Loggers = new string[]
                    {
                        $"trx;LogFileName={project.GetFilenameWithoutExtension()}.trx",
                        $"html;LogFileName={project.GetFilenameWithoutExtension()}.html",
                    },
                    // NoBuild = true, // Workaround for inconsistency between windows and linux
                    NoRestore = true,
                    ResultsDirectory = artefactsDirectory,
                });
        });

Task("Pack")
    .Description("Creates NuGet packages and outputs them to the artefacts directory.")
    .Does(() =>
    {
        DotNetPack(
            ".",
            new DotNetPackSettings()
            {
                Configuration = configuration,
                IncludeSymbols = true,
                MSBuildSettings = new DotNetMSBuildSettings()
                {
                    ContinuousIntegrationBuild = !BuildSystem.IsLocalBuild,
                },
                // NoBuild = true, // Workaround for inconsistency between windows and linux
                NoRestore = true,
                OutputDirectory = artefactsDirectory,
            });
    });

Task("Default")
    .Description("Cleans, restores NuGet packages, builds the solution, runs unit tests and then creates NuGet packages.")
    .IsDependentOn("Build")
    .IsDependentOn("Test")
    .IsDependentOn("Pack");

RunTarget(target);