using Microsoft.Extensions.Logging;

namespace Siniestros360.Messaging.Idempotency;

// Efectos externos de un comando que la base no puede revertir (por ejemplo, un archivo ya subido).
// El handler registra cómo deshacerlos; el filtro idempotente los ejecuta si la transacción se revierte.
public sealed class RollbackActions(ILogger<RollbackActions> logger)
{
    private readonly List<(string Description, Func<CancellationToken, Task> Action)> _actions = [];

    public void Register(string description, Func<CancellationToken, Task> action) => _actions.Add((description, action));

    internal async Task RunAsync(CancellationToken cancellationToken)
    {
        foreach (var (description, action) in Enumerable.Reverse(_actions))
        {
            try
            {
                await action(cancellationToken);
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Compensating action failed after rollback: {Description}", description);
            }
        }

        _actions.Clear();
    }
}
