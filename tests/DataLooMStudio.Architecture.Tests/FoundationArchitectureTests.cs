using System.Text.Json;
using System.Xml.Linq;

using DataLooMStudio.Infrastructure.DependencyInjection;
using DataLooMStudio.Modules.AiGovernance;
using DataLooMStudio.Modules.Evidence;
using DataLooMStudio.Modules.IdentityAccess;
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
    public void Api_evidence_endpoint_must_not_bypass_application_or_persistence_boundaries()
    {
        var evidenceEndpoint = Path.Combine(
            RepositoryRoot,
            "src",
            "Api",
            "DataLooMStudio.Api",
            "Endpoints",
            "EvidenceEndpoints.cs");
        var apiSource = new[]
            {
                evidenceEndpoint
            }
            .Select(file => new
            {
                File = file,
                Source = File.ReadAllText(file)
            })
            .ToArray();
        var forbiddenTokens = new[]
        {
            "DataLooMDbContext",
            "DbSet<",
            "Database.",
            "AzureEvidenceBlobStore",
            "BlobClient",
            "BlobServiceClient"
        };
        var violations = apiSource
            .Select(file => new
            {
                file.File,
                Tokens = forbiddenTokens
                    .Where(token => file.Source.Contains(token, StringComparison.Ordinal))
                    .ToArray()
            })
            .Where(result => result.Tokens.Length > 0)
            .Select(result => $"{RelativeToRepository(result.File)} contains {string.Join(", ", result.Tokens)}")
            .ToArray();

        Assert.Empty(violations);
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
        Assert.Contains(manifest.Responsibilities, responsibility => responsibility.Contains("review and decision", StringComparison.OrdinalIgnoreCase));
        Assert.True(manifest.OwnsTransactionalOutbox);
    }

    [Fact]
    public void Evidence_review_and_decision_rules_must_be_owned_by_evidence_module()
    {
        var evidenceModuleRoot = Path.Combine(
            RepositoryRoot,
            "src",
            "Modules",
            "Evidence",
            "DataLooMStudio.Modules.Evidence");
        var reviewPolicy = Path.Combine(evidenceModuleRoot, "EvidenceReviewPolicy.cs");
        var decisionPolicy = Path.Combine(evidenceModuleRoot, "EvidenceDecisionPolicy.cs");
        var moduleSources = new[] { reviewPolicy, decisionPolicy };
        var forbiddenRuleOwnerRoots = new[]
        {
            Path.Combine(RepositoryRoot, "src", "Api"),
            Path.Combine(RepositoryRoot, "src", "Runtime", "DataLooMStudio.Runtime.Persistence")
        };
        var forbiddenPolicyDefinitions = forbiddenRuleOwnerRoots
            .SelectMany(GetSourceFiles)
            .Where(file =>
            {
                var source = File.ReadAllText(file);
                return source.Contains("class EvidenceReviewPolicy", StringComparison.Ordinal)
                    || source.Contains("class EvidenceDecisionPolicy", StringComparison.Ordinal);
            })
            .Select(RelativeToRepository)
            .ToArray();
        var forbiddenImports = moduleSources
            .Select(file => new
            {
                File = file,
                Source = File.ReadAllText(file)
            })
            .Where(file =>
                file.Source.Contains("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                || file.Source.Contains("Microsoft.AspNetCore", StringComparison.Ordinal)
                || file.Source.Contains("DataLooMStudio.Runtime", StringComparison.Ordinal))
            .Select(file => RelativeToRepository(file.File))
            .ToArray();

        Assert.True(File.Exists(reviewPolicy), RelativeToRepository(reviewPolicy));
        Assert.True(File.Exists(decisionPolicy), RelativeToRepository(decisionPolicy));
        Assert.Contains("EvidenceReviewPolicy", File.ReadAllText(reviewPolicy), StringComparison.Ordinal);
        Assert.Contains("EvidenceDecisionPolicy", File.ReadAllText(decisionPolicy), StringComparison.Ordinal);
        Assert.Empty(forbiddenPolicyDefinitions);
        Assert.Empty(forbiddenImports);
    }

    [Fact]
    public void Identity_access_module_must_own_product_authority_policy()
    {
        var manifest = new IdentityAccessModule().Manifest;
        var identityModuleRoot = Path.Combine(
            RepositoryRoot,
            "src",
            "Modules",
            "IdentityAccess",
            "DataLooMStudio.Modules.IdentityAccess");
        var productAuthorityPolicy = Path.Combine(identityModuleRoot, "ProductAuthorityPolicy.cs");
        var productAuthorityPermissions = Path.Combine(identityModuleRoot, "ProductAuthorityPermissions.cs");
        var source = string.Join(
            Environment.NewLine,
            new[] { productAuthorityPolicy, productAuthorityPermissions }.Select(File.ReadAllText));
        var forbiddenImports = new[]
            {
                productAuthorityPolicy,
                productAuthorityPermissions
            }
            .Where(file =>
            {
                var fileSource = File.ReadAllText(file);
                return fileSource.Contains("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
                    || fileSource.Contains("Microsoft.AspNetCore", StringComparison.Ordinal)
                    || fileSource.Contains("DataLooMStudio.Runtime", StringComparison.Ordinal);
            })
            .Select(RelativeToRepository)
            .ToArray();

        Assert.Equal(ModuleBoundaryKind.IdentityAccess, manifest.BoundaryKind);
        Assert.True(manifest.RequiresTenantContext);
        Assert.True(manifest.RequiresWorkspaceContext);
        Assert.True(manifest.OwnsTransactionalOutbox);
        Assert.False(manifest.ContainsAiExecution);
        Assert.Contains(manifest.Responsibilities, responsibility => responsibility.Contains("Canonical permission", StringComparison.Ordinal));
        Assert.Contains("CanUsePermission", source, StringComparison.Ordinal);
        Assert.Contains("CanSatisfySeparationOfDuty", source, StringComparison.Ordinal);
        Assert.Contains("EvidenceReview.CandidateDecision.Create", source, StringComparison.Ordinal);
        Assert.Contains("EvidenceReview.Decision.Apply", source, StringComparison.Ordinal);
        Assert.Empty(forbiddenImports);
    }

    [Fact]
    public void Product_authority_taxonomy_must_match_canonical_product_decision()
    {
        var decisionRecord = Path.Combine(
            RepositoryRoot,
            "governance",
            "product-authority",
            "DLS-PROD-AUTH-001.md");
        var expectedRoles = new[]
        {
            ProductAuthorityRoleNames.TenantOwner,
            ProductAuthorityRoleNames.WorkspaceOwner,
            ProductAuthorityRoleNames.EvidenceContributor,
            ProductAuthorityRoleNames.EvidenceReader,
            ProductAuthorityRoleNames.Reviewer,
            ProductAuthorityRoleNames.DecisionApprover,
            ProductAuthorityRoleNames.GovernanceAdministrator,
            ProductAuthorityRoleNames.RetentionAdministrator,
            ProductAuthorityRoleNames.LegalHoldAdministrator,
            ProductAuthorityRoleNames.CommercialAdministrator,
            ProductAuthorityRoleNames.BillingAdministrator,
            ProductAuthorityRoleNames.SupportOperator,
            ProductAuthorityRoleNames.SecurityOperator,
            ProductAuthorityRoleNames.RepositoryAdministrator,
            ProductAuthorityRoleNames.PlatformAdministrator,
            ProductAuthorityRoleNames.Auditor
        };
        var actualRoles = ProductAuthorityRoleTaxonomy.Roles.Select(role => role.RoleName).ToArray();
        var decisionSource = File.ReadAllText(decisionRecord);

        Assert.Equal("DLS-PROD-AUTH-001", ProductAuthorityRoleTaxonomy.DecisionId);
        Assert.Equal(expectedRoles, actualRoles);
        Assert.Equal(expectedRoles.Length, actualRoles.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ProductAuthorityRoleTaxonomy.Roles.SelectMany(role => role.PermissionBundle), permission =>
            Assert.True(ProductAuthorityPermissions.IsSupported(permission), permission));
        Assert.All(expectedRoles, role => Assert.Contains(role, decisionSource, StringComparison.Ordinal));
        Assert.Contains("permissions remain the stable runtime authority contract", decisionSource, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Product_authority_role_classes_must_not_imply_content_review_or_decision_authority()
    {
        var rolesWithoutImplicitContentOrApproval = new[]
        {
            ProductAuthorityRoleNames.TenantOwner,
            ProductAuthorityRoleNames.WorkspaceOwner,
            ProductAuthorityRoleNames.CommercialAdministrator,
            ProductAuthorityRoleNames.BillingAdministrator,
            ProductAuthorityRoleNames.SupportOperator,
            ProductAuthorityRoleNames.SecurityOperator,
            ProductAuthorityRoleNames.RepositoryAdministrator,
            ProductAuthorityRoleNames.PlatformAdministrator
        };
        var productBusinessRoles = ProductAuthorityRoleTaxonomy.Roles
            .Where(role => role.RoleClass.Equals(ProductAuthorityRoleClasses.ProductBusinessRole, StringComparison.Ordinal))
            .Select(role => role.RoleName)
            .ToArray();
        var privilegedTechnicalOrOperational = ProductAuthorityRoleTaxonomy.Roles
            .Where(role => role.IsPrivilegedTechnicalOrOperational)
            .Select(role => role.RoleName)
            .ToArray();

        Assert.Contains(ProductAuthorityRoleNames.Reviewer, productBusinessRoles);
        Assert.Contains(ProductAuthorityRoleNames.DecisionApprover, productBusinessRoles);
        Assert.Contains(ProductAuthorityRoleNames.SupportOperator, privilegedTechnicalOrOperational);
        Assert.Contains(ProductAuthorityRoleNames.SecurityOperator, privilegedTechnicalOrOperational);
        Assert.Contains(ProductAuthorityRoleNames.RepositoryAdministrator, privilegedTechnicalOrOperational);
        Assert.Contains(ProductAuthorityRoleNames.PlatformAdministrator, privilegedTechnicalOrOperational);

        foreach (var roleName in rolesWithoutImplicitContentOrApproval)
        {
            var role = ProductAuthorityRoleTaxonomy.FindRole(roleName)
                ?? throw new InvalidOperationException(roleName);

            Assert.DoesNotContain(role.PermissionBundle, ProductAuthorityPermissions.IsEvidenceContentPermission);
            Assert.DoesNotContain(role.PermissionBundle, ProductAuthorityPermissions.IsEvidenceReviewOrDecisionPermission);
            Assert.DoesNotContain(role.PermissionBundle, ProductAuthorityPermissions.IsRetentionOrLegalHoldPermission);
        }
    }

    [Fact]
    public void Product_role_taxonomy_must_not_be_reintroduced_inside_evidence_or_api_boundaries()
    {
        var evidenceSource = string.Join(
            Environment.NewLine,
            GetSourceFiles(Path.Combine(RepositoryRoot, "src", "Modules", "Evidence")).Select(File.ReadAllText));
        var retentionSource = string.Join(
            Environment.NewLine,
            GetSourceFiles(Path.Combine(RepositoryRoot, "src", "Modules", "Retention")).Select(File.ReadAllText));
        var apiSource = string.Join(
            Environment.NewLine,
            GetSourceFiles(Path.Combine(RepositoryRoot, "src", "Api")).Select(File.ReadAllText));
        var policySource = File.ReadAllText(Path.Combine(
            RepositoryRoot,
            "src",
            "Modules",
            "IdentityAccess",
            "DataLooMStudio.Modules.IdentityAccess",
            "ProductAuthorityPolicy.cs"));

        Assert.DoesNotContain("ProductAuthorityRoleTaxonomy", evidenceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductAuthorityRoleNames", evidenceSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductAuthorityRoleTaxonomy", retentionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductAuthorityRoleNames", retentionSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductAuthorityRoleTaxonomy", apiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductAuthorityRoleNames", apiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("PermissionBundle", policySource, StringComparison.Ordinal);
        Assert.DoesNotContain("FindRole", policySource, StringComparison.Ordinal);
    }

    [Fact]
    public void Retention_release_and_deletion_eligibility_must_not_enable_physical_deletion()
    {
        var retentionModuleSource = string.Join(
            Environment.NewLine,
            GetSourceFiles(Path.Combine(RepositoryRoot, "src", "Modules", "Retention")).Select(File.ReadAllText));
        var retentionRuntimeSource = string.Join(
            Environment.NewLine,
            GetSourceFiles(Path.Combine(RepositoryRoot, "src", "Runtime", "DataLooMStudio.Runtime.Persistence", "Retention")).Select(File.ReadAllText));
        var apiSource = string.Join(
            Environment.NewLine,
            GetSourceFiles(Path.Combine(RepositoryRoot, "src", "Api")).Select(File.ReadAllText));
        var webSource = string.Join(
            Environment.NewLine,
            GetSourceFiles(Path.Combine(RepositoryRoot, "src", "Web")).Select(File.ReadAllText));

        Assert.Contains("DeletionEligibilityPolicy", retentionModuleSource, StringComparison.Ordinal);
        Assert.Contains("DeletionEligibilityPolicy.Evaluate", retentionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("RequestLegalHoldReleaseAsync", retentionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("ApproveLegalHoldReleaseAsync", retentionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("EvaluateDeletionEligibilityAsync", retentionRuntimeSource, StringComparison.Ordinal);
        Assert.Contains("EvidencePhysicallyDeleted: false", retentionRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("EvidenceRecords.Remove", retentionRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("EvidenceVersions.Remove", retentionRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("RemoveRange", retentionRuntimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DeleteEvidence", apiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DeletionEligibilityPolicy", apiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("DeletionEligibilityPolicy", webSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductAuthorityPermissions", webSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Evidence_review_runtime_must_delegate_authority_to_module_policies_and_product_authority()
    {
        var reviewService = Path.Combine(
            RepositoryRoot,
            "src",
            "Runtime",
            "DataLooMStudio.Runtime.Persistence",
            "Evidence",
            "EvidenceReviewDecisionService.cs");
        var source = File.ReadAllText(reviewService);
        var apiSource = string.Join(
            Environment.NewLine,
            GetSourceFiles(Path.Combine(RepositoryRoot, "src", "Api")).Select(File.ReadAllText));

        Assert.Contains("EvidenceReviewPolicy.", source, StringComparison.Ordinal);
        Assert.Contains("EvidenceDecisionPolicy.", source, StringComparison.Ordinal);
        Assert.Contains("IProductAuthorityService", source, StringComparison.Ordinal);
        Assert.Contains("ProductAuthorityPermissions.", source, StringComparison.Ordinal);
        Assert.Contains("RequireProductPermissionAsync", source, StringComparison.Ordinal);
        Assert.Contains("RequireSeparationOfDutyAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DataLooMStudio.Modules", apiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("EvidenceDecisionPolicy", apiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("EvidenceReviewPolicy", apiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductAuthorityPolicy", apiSource, StringComparison.Ordinal);
        Assert.DoesNotContain("ProductAuthorityPermissions", apiSource, StringComparison.Ordinal);
    }

    [Fact]
    public void Evidence_review_authority_must_not_use_local_role_taxonomy_in_active_code()
    {
        var activeSourceRoots = new[]
        {
            Path.Combine(RepositoryRoot, "src", "Modules"),
            Path.Combine(RepositoryRoot, "src", "Runtime"),
            Path.Combine(RepositoryRoot, "src", "Api")
        };
        var forbiddenTokens = new[]
        {
            "EvidenceReviewAuthorityRoles",
            "\"EvidenceReviewer\"",
            "\"EvidenceApprover\"",
            "'EvidenceReviewer'",
            "'EvidenceApprover'"
        };
        var violations = activeSourceRoots
            .SelectMany(GetSourceFiles)
            .Where(file => !IsUnder(file, "src", "Runtime", "DataLooMStudio.Runtime.Persistence", "Migrations"))
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

    [Fact]
    public void Identity_access_security_controls_must_remain_inside_identity_access_boundary()
    {
        var identityModuleRoot = Path.Combine(
            RepositoryRoot,
            "src",
            "Modules",
            "IdentityAccess",
            "DataLooMStudio.Modules.IdentityAccess");
        var requiredFiles = new[]
        {
            "ProductAuthorityPolicyInput.cs",
            "ProductAuthorityDenyReasonCodes.cs",
            "ProductTenantMembership.cs",
            "ProductWorkspaceMembership.cs",
            "ProductAuthorityElevation.cs",
            "ProductWorkloadIdentityMatrix.cs",
            "AuthenticatedExternalPrincipal.cs",
            "ValidatedIdentityCorrelation.cs"
        };
        var forbiddenTokens = new[]
        {
            "Microsoft.EntityFrameworkCore",
            "Microsoft.AspNetCore",
            "Azure.Identity",
            "Microsoft.Identity",
            "System.Security.Claims.ClaimsPrincipal",
            "JwtBearer"
        };
        var violations = GetSourceFiles(identityModuleRoot)
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

        Assert.All(requiredFiles, file => Assert.True(File.Exists(Path.Combine(identityModuleRoot, file)), file));
        Assert.Empty(violations);
    }

    [Fact]
    public void BuildingBlocks_must_not_contain_product_authority_or_role_taxonomy()
    {
        var forbiddenTokens = new[]
        {
            "ProductAuthority",
            "ProductActor",
            "EvidenceReviewer",
            "EvidenceApprover",
            "PlatformAdmin",
            "CommercialAdmin",
            "BillingAdmin"
        };
        var violations = GetSourceFiles(Path.Combine(RepositoryRoot, "src", "BuildingBlocks"))
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

    [Fact]
    public void Commercial_entitlements_must_not_grant_review_or_decision_authority()
    {
        var forbiddenTokens = new[]
        {
            "ProductAuthorityPolicy",
            "ProductAuthorityPermissions",
            "EvidenceReview.",
            "EvidenceReviewDecision",
            "Decision.Apply"
        };
        var violations = GetSourceFiles(Path.Combine(RepositoryRoot, "src", "Modules", "Commercial"))
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

    [Fact]
    public void Product_authority_external_identity_boundary_must_be_provider_neutral()
    {
        var identityModuleRoot = Path.Combine(
            RepositoryRoot,
            "src",
            "Modules",
            "IdentityAccess",
            "DataLooMStudio.Modules.IdentityAccess");
        var boundarySources = new[]
        {
            Path.Combine(identityModuleRoot, "AuthenticatedExternalPrincipal.cs"),
            Path.Combine(identityModuleRoot, "ValidatedIdentityCorrelation.cs"),
            Path.Combine(identityModuleRoot, "ProductActorCorrelationPolicy.cs")
        };
        var source = string.Join(Environment.NewLine, boundarySources.Select(File.ReadAllText));

        Assert.Contains("AuthenticatedExternalPrincipal", source, StringComparison.Ordinal);
        Assert.Contains("ValidatedIdentityCorrelation", source, StringComparison.Ordinal);
        Assert.Contains("ProductActorSubject", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ClaimsPrincipal", source, StringComparison.Ordinal);
        Assert.DoesNotContain("JwtSecurityToken", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AccessToken", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Azure.Identity", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Workload_identity_matrix_must_prohibit_human_approval_impersonation()
    {
        var profiles = ProductWorkloadIdentityMatrix.All.ToDictionary(profile => profile.WorkloadName, StringComparer.Ordinal);

        Assert.Contains("dls-web", profiles.Keys);
        Assert.Contains("dls-worker", profiles.Keys);
        Assert.Contains("dls-migrate", profiles.Keys);
        Assert.Contains("scanner", profiles.Keys);
        Assert.Contains("reconciliation", profiles.Keys);
        Assert.Contains("support-tooling", profiles.Keys);
        Assert.All(profiles.Values, profile => Assert.False(profile.MayImpersonateHumanApprover));
        Assert.Contains(ProductAuthorityPermissions.ApplyEvidenceDecision, profiles["dls-migrate"].ProhibitedPermissions);
        Assert.Contains(ProductAuthorityPermissions.CreateEvidenceCandidateDecision, profiles["scanner"].ProhibitedPermissions);
        Assert.Contains(ProductAuthorityPermissions.ReadEvidence, profiles["support-tooling"].ProhibitedPermissions);
    }

    [Fact]
    public void Identity_access_migration_must_preserve_evidence_assignment_history()
    {
        var migration = Path.Combine(
            RepositoryRoot,
            "src",
            "Runtime",
            "DataLooMStudio.Runtime.Persistence",
            "Migrations",
            "20260812164608_IdentityAccessProductAuthority.cs");
        var source = File.ReadAllText(migration);

        Assert.Contains("RenameColumn", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DropColumn(\r\n                name: \"Role\"", source, StringComparison.Ordinal);
        Assert.Contains("when 'EvidenceReviewer' then 'EvidenceReview.CandidateDecision.Create'", source, StringComparison.Ordinal);
        Assert.Contains("when 'EvidenceApprover' then 'EvidenceReview.Decision.Apply'", source, StringComparison.Ordinal);
        Assert.Contains("insert into identity_access.product_actors", source, StringComparison.Ordinal);
        Assert.Contains("insert into identity_access.product_permission_assignments", source, StringComparison.Ordinal);
        Assert.Contains("enable row level security", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CK_product_permission_assignments_permission_key", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Identity_access_security_migration_must_seed_memberships_and_enforce_rls()
    {
        var migration = Path.Combine(
            RepositoryRoot,
            "src",
            "Runtime",
            "DataLooMStudio.Runtime.Persistence",
            "Migrations",
            "20260812183021_IdentityAccessSecurityControls.cs");
        var source = File.ReadAllText(migration);

        Assert.Contains("insert into identity_access.product_tenant_memberships", source, StringComparison.Ordinal);
        Assert.Contains("insert into identity_access.product_workspace_memberships", source, StringComparison.Ordinal);
        Assert.Contains("alter table identity_access.product_tenant_memberships enable row level security", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("alter table identity_access.product_workspace_memberships enable row level security", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("alter table identity_access.product_authority_elevations enable row level security", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("CK_product_authority_elevations_effective_window", source, StringComparison.Ordinal);
        Assert.Contains("CK_product_permission_assignments_authority_version", source, StringComparison.Ordinal);
        Assert.Contains("Evidence.Read.Restricted", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Architecture_erd_conditions_must_be_carried_as_governance_evidence()
    {
        var conditionRecord = Path.Combine(
            RepositoryRoot,
            "governance",
            "architecture-conditions",
            "ARCH-ERD-001-through-005.md");
        var source = File.ReadAllText(conditionRecord);

        Assert.Contains("ARCH-ERD-001", source, StringComparison.Ordinal);
        Assert.Contains("ARCH-ERD-002", source, StringComparison.Ordinal);
        Assert.Contains("ARCH-ERD-003", source, StringComparison.Ordinal);
        Assert.Contains("ARCH-ERD-004", source, StringComparison.Ordinal);
        Assert.Contains("ARCH-ERD-005", source, StringComparison.Ordinal);
        Assert.Contains("material condition", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DLS-INC-002-EVID-001", source, StringComparison.Ordinal);
        Assert.Contains("DLS-INC-002-PROD-DEC-001", source, StringComparison.Ordinal);
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
    public void Runtime_persistence_must_not_take_blob_or_ai_execution_dependencies()
    {
        var persistenceProject = Path.Combine(
            RepositoryRoot,
            "src",
            "Runtime",
            "DataLooMStudio.Runtime.Persistence",
            "DataLooMStudio.Runtime.Persistence.csproj");
        var forbiddenPackagePrefixes = new[]
        {
            "Azure.Storage.Blobs",
            "Azure.AI",
            "OpenAI",
            "Microsoft.SemanticKernel"
        };
        var forbiddenSourceTokens = new[]
        {
            "BlobClient",
            "BlobServiceClient",
            "ChatClient",
            "OpenAIClient",
            "SemanticKernel"
        };
        var packageViolations = GetPackageReferences(persistenceProject)
            .Where(package => forbiddenPackagePrefixes.Any(prefix =>
                package.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
        var sourceViolations = GetSourceFiles(Path.GetDirectoryName(persistenceProject)!)
            .Select(file => new
            {
                File = file,
                Tokens = forbiddenSourceTokens
                    .Where(token => File.ReadAllText(file).Contains(token, StringComparison.Ordinal))
                    .ToArray()
            })
            .Where(result => result.Tokens.Length > 0)
            .Select(result => $"{RelativeToRepository(result.File)} contains {string.Join(", ", result.Tokens)}")
            .ToArray();

        Assert.Empty(packageViolations);
        Assert.Empty(sourceViolations);
    }

    [Fact]
    public void Product_modules_must_not_reference_storage_or_scanning_provider_dependencies()
    {
        var moduleProjects = GetProjectFiles(Path.Combine(RepositoryRoot, "src", "Modules"));
        var forbiddenPackagePrefixes = new[]
        {
            "Azure.Storage",
            "Azure.AI",
            "OpenAI",
            "Microsoft.SemanticKernel"
        };
        var forbiddenSourceTokens = new[]
        {
            "BlobClient",
            "BlobServiceClient",
            "BlobSasBuilder",
            "TokenCredential",
            "AzureEvidenceObjectStore",
            "DefaultAzureCredential",
            "OpenAIClient",
            "ChatClient",
            "SemanticKernel"
        };
        var packageViolations = moduleProjects
            .SelectMany(project => GetPackageReferences(project)
                .Where(package => forbiddenPackagePrefixes.Any(prefix =>
                    package.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                .Select(package => $"{RelativeToRepository(project)} uses {package}"))
            .ToArray();
        var sourceViolations = GetSourceFiles(Path.Combine(RepositoryRoot, "src", "Modules"))
            .Select(file => new
            {
                File = file,
                Tokens = forbiddenSourceTokens
                    .Where(token => File.ReadAllText(file).Contains(token, StringComparison.Ordinal))
                    .ToArray()
            })
            .Where(result => result.Tokens.Length > 0)
            .Select(result => $"{RelativeToRepository(result.File)} contains {string.Join(", ", result.Tokens)}")
            .ToArray();

        Assert.Empty(packageViolations);
        Assert.Empty(sourceViolations);
    }

    [Fact]
    public void Evidence_storage_and_scanning_adapters_must_remain_provider_neutral_outside_infrastructure()
    {
        var infrastructureRoot = Path.Combine(RepositoryRoot, "src", "BuildingBlocks", "DataLooMStudio.Infrastructure");
        var azureStorageProvider = Path.Combine(infrastructureRoot, "Storage", "AzureEvidenceObjectStore.cs");
        var scannerBoundary = Path.Combine(infrastructureRoot, "SecurityScanning", "IEvidenceMalwareScanner.cs");
        var forbiddenProviderTokens = new[]
        {
            "BlobClient",
            "BlobServiceClient",
            "BlobSasBuilder",
            "DefaultAzureCredential",
            "TokenCredential"
        };
        var nonInfrastructureSource = GetSourceFiles(Path.Combine(RepositoryRoot, "src"))
            .Where(file => !IsUnder(file, "src", "BuildingBlocks", "DataLooMStudio.Infrastructure"))
            .ToArray();
        var violations = nonInfrastructureSource
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

        Assert.True(File.Exists(azureStorageProvider), RelativeToRepository(azureStorageProvider));
        Assert.True(File.Exists(scannerBoundary), RelativeToRepository(scannerBoundary));
        Assert.Empty(violations);
    }

    [Fact]
    public void Product_audit_for_evidence_content_must_not_be_implemented_through_logging()
    {
        var evidenceRuntimeSource = GetSourceFiles(Path.Combine(
            RepositoryRoot,
            "src",
            "Runtime",
            "DataLooMStudio.Runtime.Persistence",
            "Evidence"));
        var forbiddenTokens = new[]
        {
            "ILogger",
            "LogInformation",
            "LogWarning",
            "LogError"
        };
        var violations = evidenceRuntimeSource
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

    [Fact]
    public void Product_code_must_not_contain_storage_credentials()
    {
        var productRoots = new[]
        {
            Path.Combine(RepositoryRoot, "src", "Modules"),
            Path.Combine(RepositoryRoot, "src", "Runtime"),
            Path.Combine(RepositoryRoot, "src", "Api")
        };
        var credentialPatterns = new[]
        {
            "AccountKey=",
            "SharedAccessSignature=",
            "DefaultEndpointsProtocol=",
            "BlobEndpoint=",
            "QueueEndpoint="
        };
        var violations = productRoots
            .SelectMany(GetSourceFiles)
            .Select(file => new
            {
                File = file,
                Tokens = credentialPatterns
                    .Where(token => File.ReadAllText(file).Contains(token, StringComparison.OrdinalIgnoreCase))
                    .ToArray()
            })
            .Where(result => result.Tokens.Length > 0)
            .Select(result => $"{RelativeToRepository(result.File)} contains {string.Join(", ", result.Tokens)}")
            .ToArray();

        Assert.Empty(violations);
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