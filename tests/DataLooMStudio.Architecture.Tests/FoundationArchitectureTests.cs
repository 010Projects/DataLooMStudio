using System.Text.Json;
using System.Xml.Linq;

using DataLooMStudio.Infrastructure.DependencyInjection;
using DataLooMStudio.Modules.AiGovernance;
using DataLooMStudio.Modules.Evidence;
using DataLooMStudio.Modules.Lifecycle;
using DataLooMStudio.Modules.Workflows;
using DataLooMStudio.Runtime.Persistence.Migrations;
using DataLooMStudio.SharedKernel.Modules;
using DataLooMStudio.SharedKernel.RequestContext;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DataLooMStudio.Architecture.Tests;

public sealed class FoundationArchitectureTests
{
    private static readonly string RepositoryRoot = FindRepositoryRoot();

    [Fact]
    public void Operations_module_must_not_exist()
    {
        var operationsPath = Path.Combine(RepositoryRoot, "src", "Modules", "Operations");

        Assert.False(Directory.Exists(operationsPath));
    }

    [Fact]
    public void Every_module_folder_has_manifest()
    {
        var moduleFolders = Directory.GetDirectories(Path.Combine(RepositoryRoot, "src", "Modules"));

        Assert.NotEmpty(moduleFolders);
        foreach (var moduleFolder in moduleFolders)
        {
            var manifestPath = Path.Combine(moduleFolder, "module.manifest.json");
            Assert.True(File.Exists(manifestPath), $"Missing manifest: {manifestPath}");

            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            Assert.True(document.RootElement.TryGetProperty("name", out _));
            Assert.True(document.RootElement.TryGetProperty("boundaryKind", out _));
        }
    }

    [Fact]
    public void Runtime_boundaries_are_the_only_source_projects_allowed_to_compose_modules()
    {
        var sourceProjects = GetProjectFiles(Path.Combine(RepositoryRoot, "src"));
        var violations = sourceProjects
            .Where(project => !IsUnder(project, "src", "Modules") && !IsUnder(project, "src", "Runtime"))
            .SelectMany(project => GetProjectReferences(project)
                .Where(reference => IsUnder(reference, "src", "Modules"))
                .Select(reference => $"{RelativeToRepository(project)} -> {RelativeToRepository(reference)}"))
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void BuildingBlocks_projects_must_not_reference_runtime_api_or_modules()
    {
        var buildingBlockProjects = GetProjectFiles(Path.Combine(RepositoryRoot, "src", "BuildingBlocks"));
        var violations = buildingBlockProjects
            .SelectMany(project => GetProjectReferences(project)
                .Where(reference =>
                    IsUnder(reference, "src", "Modules")
                    || IsUnder(reference, "src", "Runtime")
                    || IsUnder(reference, "src", "Api"))
                .Select(reference => $"{RelativeToRepository(project)} -> {RelativeToRepository(reference)}"))
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void BuildingBlocks_must_not_own_runtime_persistence_dependencies()
    {
        var buildingBlockProjects = GetProjectFiles(Path.Combine(RepositoryRoot, "src", "BuildingBlocks"));
        var forbiddenPackages = new[] { "Microsoft.EntityFrameworkCore", "Npgsql.EntityFrameworkCore.PostgreSQL" };
        var violations = buildingBlockProjects
            .SelectMany(project => GetPackageReferences(project)
                .Where(package => forbiddenPackages.Any(forbidden =>
                    package.Equals(forbidden, StringComparison.OrdinalIgnoreCase)
                    || package.StartsWith($"{forbidden}.", StringComparison.OrdinalIgnoreCase)))
                .Select(package => $"{RelativeToRepository(project)} uses {package}"))
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void Module_projects_must_depend_only_on_building_blocks()
    {
        var moduleProjects = GetProjectFiles(Path.Combine(RepositoryRoot, "src", "Modules"));
        var violations = moduleProjects
            .SelectMany(project => GetProjectReferences(project)
                .Where(reference => !IsUnder(reference, "src", "BuildingBlocks"))
                .Select(reference => $"{RelativeToRepository(project)} -> {RelativeToRepository(reference)}"))
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void Module_projects_must_not_reference_runtime_or_platform_packages()
    {
        var moduleProjects = GetProjectFiles(Path.Combine(RepositoryRoot, "src", "Modules"));
        var forbiddenPackagePrefixes = new[]
        {
            "Azure.",
            "Microsoft.AspNetCore",
            "Microsoft.EntityFrameworkCore",
            "Npgsql",
            "OpenTelemetry"
        };
        var violations = moduleProjects
            .SelectMany(project => GetPackageReferences(project)
                .Where(package => forbiddenPackagePrefixes.Any(prefix =>
                    package.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                .Select(package => $"{RelativeToRepository(project)} uses {package}"))
            .ToArray();

        Assert.Empty(violations);
    }

    [Fact]
    public void Api_project_must_not_reference_or_import_modules_directly()
    {
        var apiProject = Path.Combine(
            RepositoryRoot,
            "src",
            "Api",
            "DataLooMStudio.Api",
            "DataLooMStudio.Api.csproj");
        var directModuleReferences = GetProjectReferences(apiProject)
            .Where(reference => IsUnder(reference, "src", "Modules"))
            .Select(RelativeToRepository)
            .ToArray();
        var moduleImports = GetSourceFiles(Path.Combine(RepositoryRoot, "src", "Api"))
            .Where(file => File.ReadAllText(file).Contains("DataLooMStudio.Modules", StringComparison.Ordinal))
            .Select(RelativeToRepository)
            .ToArray();

        Assert.Empty(directModuleReferences);
        Assert.Empty(moduleImports);
    }

    [Fact]
    public void Distinct_worker_runtime_must_not_depend_on_api_or_migration_runtime()
    {
        var workerProject = Path.Combine(
            RepositoryRoot,
            "src",
            "Dls.Worker",
            "DataLooMStudio.Dls.Worker",
            "DataLooMStudio.Dls.Worker.csproj");
        var references = GetProjectReferences(workerProject).Select(RelativeToRepository).ToArray();
        var workerSource = string.Join(
            Environment.NewLine,
            GetSourceFiles(Path.GetDirectoryName(workerProject)!).Select(File.ReadAllText));

        Assert.True(File.Exists(workerProject));
        Assert.DoesNotContain(references, reference => reference.Contains("src\\Api", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, reference => reference.Contains("src\\Dls.Migrate", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("WebApplication", workerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("MapGet", workerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("Database.Migrate", workerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureCreated", workerSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Worker_runtime_must_register_scoped_request_context_for_context_propagation()
    {
        var workerProgram = Path.Combine(
            RepositoryRoot,
            "src",
            "Dls.Worker",
            "DataLooMStudio.Dls.Worker",
            "Program.cs");
        var workerProgramSource = File.ReadAllText(workerProgram);
        var services = new ServiceCollection();

        services.AddDataLooMInfrastructure(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider(validateScopes: true);
        using var scope = provider.CreateScope();
        using var secondScope = provider.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IRequestContextAccessor>();

        Assert.Contains("AddDataLooMInfrastructure", workerProgramSource, StringComparison.Ordinal);
        Assert.Same(accessor, scope.ServiceProvider.GetRequiredService<IRequestContextAccessor>());
        Assert.NotSame(accessor, secondScope.ServiceProvider.GetRequiredService<IRequestContextAccessor>());
    }

    [Fact]
    public void Migration_runtime_must_be_separate_from_api_and_worker_startup()
    {
        var migrationProject = Path.Combine(
            RepositoryRoot,
            "src",
            "Dls.Migrate",
            "DataLooMStudio.Dls.Migrate",
            "DataLooMStudio.Dls.Migrate.csproj");
        var references = GetProjectReferences(migrationProject).Select(RelativeToRepository).ToArray();
        var apiAndWorkerStartupSource = new[]
            {
                Path.Combine(RepositoryRoot, "src", "Api"),
                Path.Combine(RepositoryRoot, "src", "Dls.Worker")
            }
            .SelectMany(GetSourceFiles)
            .Select(File.ReadAllText)
            .Aggregate(string.Empty, (current, next) => current + Environment.NewLine + next);

        Assert.True(File.Exists(migrationProject));
        Assert.DoesNotContain(references, reference => reference.Contains("src\\Api", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(references, reference => reference.Contains("src\\Dls.Worker", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain("Database.Migrate", apiAndWorkerStartupSource, StringComparison.Ordinal);
        Assert.DoesNotContain("EnsureCreated", apiAndWorkerStartupSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Ai_governance_is_boundary_only()
    {
        var manifest = new AiGovernanceModule().Manifest;

        Assert.Equal(ModuleBoundaryKind.AiGovernanceBoundary, manifest.BoundaryKind);
        Assert.False(manifest.ContainsAiExecution);
    }

    [Fact]
    public void Ai_governance_boundary_must_not_include_provider_or_execution_clients()
    {
        var aiProject = Path.Combine(
            RepositoryRoot,
            "src",
            "Modules",
            "AiGovernance",
            "DataLooMStudio.Modules.AiGovernance",
            "DataLooMStudio.Modules.AiGovernance.csproj");
        var forbiddenPackages = GetPackageReferences(aiProject).ToArray();
        var forbiddenProviderTokens = new[]
        {
            "Azure.AI",
            "AzureOpenAI",
            "ChatClient",
            "IChatCompletionService",
            "Microsoft.SemanticKernel",
            "OpenAI",
            "SemanticKernel"
        };
        var tokenViolations = GetSourceFiles(Path.GetDirectoryName(aiProject)!)
            .Select(file => new
            {
                File = file,
                Tokens = forbiddenProviderTokens
                    .Where(token => File.ReadAllText(file).Contains(token, StringComparison.Ordinal))
                    .ToArray()
            })
            .Where(result => result.Tokens.Length > 0)
            .Select(result => $"{RelativeToRepository(result.File)} contains {string.Join(", ", result.Tokens)}")
            .ToArray();

        Assert.Empty(forbiddenPackages);
        Assert.Empty(tokenViolations);
    }

    [Fact]
    public void Lifecycle_and_workflow_modules_are_separate_boundaries()
    {
        var lifecycle = new LifecycleModule().Manifest;
        var workflows = new WorkflowsModule().Manifest;

        Assert.Equal(ModuleBoundaryKind.Lifecycle, lifecycle.BoundaryKind);
        Assert.Equal(ModuleBoundaryKind.Workflow, workflows.BoundaryKind);
        Assert.NotEqual(lifecycle.Name, workflows.Name);
    }

    [Fact]
    public void Evidence_module_owns_adr_014_consistency_boundary()
    {
        var manifest = new EvidenceModule().Manifest;

        Assert.Equal(ModuleBoundaryKind.EvidenceConsistency, manifest.BoundaryKind);
        Assert.Contains(manifest.Responsibilities, responsibility => responsibility.Contains("ADR-014", StringComparison.Ordinal));
        Assert.True(manifest.OwnsTransactionalOutbox);
    }

    [Fact]
    public void Runtime_persistence_must_own_db_context_and_module_migration_boundaries()
    {
        var contextOwners = GetSourceFiles(Path.Combine(RepositoryRoot, "src"))
            .Where(file =>
            {
                var source = File.ReadAllText(file);
                return source.Contains(": DbContext", StringComparison.Ordinal)
                    || source.Contains("DbContextOptions<", StringComparison.Ordinal);
            })
            .Where(file => !IsUnder(file, "src", "Runtime", "DataLooMStudio.Runtime.Persistence"))
            .Select(RelativeToRepository)
            .ToArray();

        Assert.Empty(contextOwners);
    }

    [Fact]
    public void Migration_boundaries_are_isolated_by_approved_module_schema()
    {
        var expected = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["IdentityAccess"] = "identity_access",
            ["WorkspaceWeave"] = "workspace_weave",
            ["Evidence"] = "evidence",
            ["AuditLineage"] = "audit_lineage",
            ["Retention"] = "retention",
            ["Commercial"] = "commercial",
            ["Lifecycle"] = "lifecycle",
            ["Workflows"] = "workflow",
            ["AiGovernance"] = "ai_governance"
        };
        var boundaries = ModuleMigrationCatalog.Boundaries;
        var moduleMigrationFiles = GetSourceFiles(Path.Combine(RepositoryRoot, "src", "Modules"))
            .Where(file => Path.GetFileName(file).Contains("Migration", StringComparison.OrdinalIgnoreCase))
            .Select(RelativeToRepository)
            .ToArray();

        Assert.Equal(expected.Count, boundaries.Count);
        Assert.Empty(moduleMigrationFiles);
        Assert.DoesNotContain(boundaries, boundary =>
            boundary.ModuleName.Equals("Operations", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(expected.Keys.Order(StringComparer.Ordinal), boundaries.Select(boundary => boundary.ModuleName).Order(StringComparer.Ordinal));
        Assert.Equal(expected.Values.Order(StringComparer.Ordinal), boundaries.Select(boundary => boundary.SchemaName).Order(StringComparer.Ordinal));
        Assert.Equal(boundaries.Count, boundaries.Select(boundary => boundary.SchemaName).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(boundaries.Count, boundaries.Select(boundary => boundary.MigrationsNamespace).Distinct(StringComparer.Ordinal).Count());
        Assert.All(boundaries, boundary =>
        {
            Assert.Equal(expected[boundary.ModuleName], boundary.SchemaName);
            Assert.StartsWith(
                "DataLooMStudio.Runtime.Persistence.Migrations.",
                boundary.MigrationsNamespace,
                StringComparison.Ordinal);
        });
    }

    [Fact]
    public void React_frontend_must_remain_separate_from_product_authority()
    {
        var forbiddenProductRoots = new[]
        {
            Path.Combine(RepositoryRoot, "src", "Product"),
            Path.Combine(RepositoryRoot, "src", "Products")
        };
        var forbiddenTokens = new[]
        {
            "DataLooMStudio.Product",
            "ProductAuthority",
            "ProductionAuthority",
            "RestrictedPilotAuthority"
        };
        var webSourceRoot = Path.Combine(RepositoryRoot, "src", "Web", "DataLooMStudio.Web", "src");
        var tokenViolations = GetTextFiles(webSourceRoot)
            .Select(file => new
            {
                File = file,
                Tokens = forbiddenTokens
                    .Where(token => File.ReadAllText(file).Contains(token, StringComparison.Ordinal))
                    .ToArray()
            })
            .Where(result => result.Tokens.Length > 0)
            .Select(result => $"{RelativeToRepository(result.File)} contains {string.Join(", ", result.Tokens)}")
            .ToArray();

        Assert.All(forbiddenProductRoots, root => Assert.False(Directory.Exists(root), RelativeToRepository(root)));
        Assert.True(File.Exists(Path.Combine(RepositoryRoot, "src", "Web", "DataLooMStudio.Web", "package.json")));
        Assert.Empty(tokenViolations);
    }

    [Fact]
    public void Historical_DataLooM_repository_assets_must_not_be_present()
    {
        var forbiddenTokens = new[]
        {
            "github.com/010Projects/" + "DataLooM.git",
            "010Projects/" + "DataLooM/",
            "010Projects\\" + "DataLooM\\",
            "DataLooM." + "Core",
            "DataLooM." + "Infrastructure",
            "DataLooM." + "Legacy"
        };
        var textFiles = Directory.GetFiles(RepositoryRoot, "*.*", SearchOption.AllDirectories)
            .Where(file => !IsGeneratedPath(file))
            .Where(file => !file.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .Where(file =>
                file.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".slnx", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var violations = textFiles
            .Select(file => new
            {
                File = file,
                Tokens = forbiddenTokens
                    .Where(token => File.ReadAllText(file).Contains(token, StringComparison.Ordinal))
                    .ToArray()
            })
            .Where(result => result.Tokens.Length > 0)
            .Select(result => $"{RelativeToRepository(result.File)} contains {string.Join(", ", result.Tokens)}")
            .ToArray();

        Assert.Empty(violations);
    }

    private static string FindRepositoryRoot()
    {
        var current = AppContext.BaseDirectory;

        while (!string.IsNullOrWhiteSpace(current))
        {
            if (File.Exists(Path.Combine(current, "DataLooMStudio.slnx")))
            {
                return current;
            }

            current = Directory.GetParent(current)?.FullName ?? string.Empty;
        }

        throw new DirectoryNotFoundException("Repository root could not be located.");
    }

    private static IReadOnlyList<string> GetProjectFiles(string root)
    {
        return Directory.GetFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(file => !IsGeneratedPath(file))
            .Select(Path.GetFullPath)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> GetSourceFiles(string root)
    {
        return Directory.GetFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(file => !IsGeneratedPath(file))
            .Select(Path.GetFullPath)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> GetTextFiles(string root)
    {
        return Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(file => !IsGeneratedPath(file))
            .Where(file =>
                file.EndsWith(".css", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".ts", StringComparison.OrdinalIgnoreCase)
                || file.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFullPath)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static IReadOnlyList<string> GetProjectReferences(string projectPath)
    {
        var projectDirectory = Path.GetDirectoryName(projectPath)
            ?? throw new DirectoryNotFoundException(projectPath);
        var document = XDocument.Load(projectPath);
        var projectReferenceName = document.Root!.Name.Namespace + "ProjectReference";

        return document
            .Descendants(projectReferenceName)
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFullPath(Path.Combine(projectDirectory, NormalizeProjectPath(include!))))
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static string NormalizeProjectPath(string path)
    {
        return path
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
    }

    private static IReadOnlyList<string> GetPackageReferences(string projectPath)
    {
        var document = XDocument.Load(projectPath);
        var packageReferenceName = document.Root!.Name.Namespace + "PackageReference";

        return document
            .Descendants(packageReferenceName)
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => include!)
            .Order(StringComparer.Ordinal)
            .ToArray();
    }

    private static bool IsUnder(string path, params string[] relativeSegments)
    {
        var root = Path.GetFullPath(Path.Combine(new[] { RepositoryRoot }.Concat(relativeSegments).ToArray()));
        var fullPath = Path.GetFullPath(path);

        return fullPath.Equals(root, StringComparison.OrdinalIgnoreCase)
            || fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsGeneratedPath(string path)
    {
        return path
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment =>
                segment.Equals("bin", StringComparison.OrdinalIgnoreCase)
                || segment.Equals("obj", StringComparison.OrdinalIgnoreCase));
    }

    private static string RelativeToRepository(string path)
    {
        return Path.GetRelativePath(RepositoryRoot, path);
    }
}