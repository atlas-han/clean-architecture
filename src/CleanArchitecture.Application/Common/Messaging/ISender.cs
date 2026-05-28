using System.Threading;
using System.Threading.Tasks;

namespace CleanArchitecture.Application.Common.Messaging
{
    public interface ISender
    {
        Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default);

        Task Send(IRequest request, CancellationToken cancellationToken = default);
    }
}
