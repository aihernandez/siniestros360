using Siniestros360.LocationSimulator.Models;

namespace Siniestros360.LocationSimulator.Publishing;

public interface ILocationPublisher
{
    Task PublishAsync(LocationBatch batch, CancellationToken cancellationToken);
}
