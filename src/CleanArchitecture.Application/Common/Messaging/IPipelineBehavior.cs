using System.Threading;
using System.Threading.Tasks;

namespace CleanArchitecture.Application.Common.Messaging
{
    public delegate Task<TResponse> RequestHandlerDelegate<TResponse>();

    public delegate Task RequestHandlerDelegate();

    public interface IPipelineBehavior<TRequest, TResponse>
        where TRequest : IRequest<TResponse>
    {
        Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken cancellationToken);
    }

    public interface IPipelineBehavior<TRequest>
        where TRequest : IRequest
    {
        Task Handle(TRequest request, RequestHandlerDelegate next, CancellationToken cancellationToken);
    }
}
