using DataLooMStudio.SharedKernel.Identity;

namespace DataLooMStudio.SharedKernel.Abstractions;

public interface ITenantScoped
{
    TenantId TenantId { get; }
}