using DataLooMStudio.SharedKernel.Identity;

namespace DataLooMStudio.SharedKernel.Abstractions;

public interface IWorkspaceScoped : ITenantScoped
{
    WorkspaceId WorkspaceId { get; }
}