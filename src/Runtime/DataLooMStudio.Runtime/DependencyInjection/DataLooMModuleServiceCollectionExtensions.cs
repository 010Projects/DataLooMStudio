using DataLooMStudio.Modules.AiGovernance;
using DataLooMStudio.Modules.Audit;
using DataLooMStudio.Modules.Commercial;
using DataLooMStudio.Modules.Evidence;
using DataLooMStudio.Modules.Lifecycle;
using DataLooMStudio.Modules.Lineage;
using DataLooMStudio.Modules.Retention;
using DataLooMStudio.Modules.Tenancy;
using DataLooMStudio.Modules.Workflows;
using DataLooMStudio.Modules.Workspaces;
using DataLooMStudio.SharedKernel.Modules;

using Microsoft.Extensions.DependencyInjection;

namespace DataLooMStudio.Runtime.DependencyInjection;

public static class DataLooMModuleServiceCollectionExtensions
{
    public static IServiceCollection AddDataLooMModules(this IServiceCollection services)
    {
        services.AddSingleton<IDataLooMModule, TenancyModule>();
        services.AddSingleton<IDataLooMModule, WorkspacesModule>();
        services.AddSingleton<IDataLooMModule, EvidenceModule>();
        services.AddSingleton<IDataLooMModule, LineageModule>();
        services.AddSingleton<IDataLooMModule, AuditModule>();
        services.AddSingleton<IDataLooMModule, RetentionModule>();
        services.AddSingleton<IDataLooMModule, CommercialModule>();
        services.AddSingleton<IDataLooMModule, LifecycleModule>();
        services.AddSingleton<IDataLooMModule, WorkflowsModule>();
        services.AddSingleton<IDataLooMModule, AiGovernanceModule>();

        return services;
    }
}