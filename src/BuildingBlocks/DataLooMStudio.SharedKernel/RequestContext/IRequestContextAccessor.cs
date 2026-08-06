namespace DataLooMStudio.SharedKernel.RequestContext;

public interface IRequestContextAccessor
{
    RequestContext? Current { get; set; }
}