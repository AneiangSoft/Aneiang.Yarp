using Microsoft.AspNetCore.Http;
using Yarp.ReverseProxy.Model;

namespace Aneiang.Yarp.Infrastructure.Resilience;

public interface IDestinationCandidateCoordinator
{
    ValueTask<IReadOnlyList<DestinationState>> ApplyAsync(HttpContext context, bool excludeAttempted, CancellationToken cancellationToken = default);
    void MarkAttempted(HttpContext context, DestinationState? destination);
}
