using DataLooMStudio.SharedKernel.RequestContext;

using KernelRequestContext = DataLooMStudio.SharedKernel.RequestContext.RequestContext;

namespace DataLooMStudio.Infrastructure.RequestContext;

public sealed class RequestContextAccessor : IRequestContextAccessor
{
    public KernelRequestContext? Current { get; set; }
}