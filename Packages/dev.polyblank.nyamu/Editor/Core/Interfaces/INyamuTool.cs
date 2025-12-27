using System.Threading.Tasks;

namespace Nyamu.Core.Interfaces
{
    // Generic tool interface with strongly-typed request and response
    // TRequest: Request DTO type
    // TResponse: Response DTO type
    public interface INyamuTool<TRequest, TResponse>
    {
        string Name { get; }
        Task<TResponse> ExecuteAsync(TRequest request, IExecutionContext context);
    }
}
