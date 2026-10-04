namespace Siniestros360.AdjustersService.Domain;

public enum AdjusterStatus { Offline, Available, Assigned, EnRoute, Arrived, InService }

public sealed class Adjuster
{
    public Guid Id { get; set; }
    public string DisplayName { get; set; } = default!;
    public bool IsAvailable { get; set; }
    public AdjusterStatus Status { get; set; }
    public Guid? ActiveClaimId { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public long Version { get; set; }

    // Devuelve el estado previo cuando hubo cambio, para publicar AdjusterStatusChanged.
    public AdjusterStatus? ChangeStatus(AdjusterStatus next, Guid? activeClaimId, DateTimeOffset at)
    {
        var previous = Status;
        ActiveClaimId = activeClaimId;
        IsAvailable = next == AdjusterStatus.Available;
        Status = next;
        UpdatedAt = at;
        Version++;
        return previous == next ? null : previous;
    }
}

// Siniestro cerrado o cancelado. Los eventos llegan fuera de orden: una asignación procesada después de la cancelación
// ataba al ajustador a un siniestro terminado. Con este registro, la asignación tardía se ignora.
public sealed class FinishedClaim
{
    public Guid ClaimId { get; set; }
    public DateTimeOffset FinishedAt { get; set; }
}

// Unidades de la demo. Sus IDs coinciden con los que usa Siniestros360.LocationSimulator (un dígito repetido 32 veces).
public static class DemoAdjusters
{
    public const int Count = 5;

    public static IEnumerable<(Guid Id, string DisplayName)> All => Enumerable.Range(0, Count)
        .Select(index => (Guid.Parse(new string((char)('1' + index), 32)), $"Ajustador {index + 1:00}"));
}
