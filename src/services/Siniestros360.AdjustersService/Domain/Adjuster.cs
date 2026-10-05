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

// Lo que Adjusters sabe de cada siniestro. La asignación (de Dispatch) y la cancelación o el cierre (de Claims) llegan
// por topics distintos, sin orden y a veces a la vez. Todos escriben esta fila: si dos se procesan en paralelo chocan en
// ella (llave duplicada o RowVersion) y el reintento decide viendo lo que el otro confirmó. Así una asignación nunca ata
// al ajustador a un siniestro ya terminado, llegue antes, después o al mismo tiempo que la cancelación.
public sealed class ClaimRecord
{
    public Guid ClaimId { get; set; }
    public Guid? AdjusterId { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public uint RowVersion { get; set; }
}

// Unidades de la demo. Sus IDs coinciden con los que usa Siniestros360.LocationSimulator (un dígito repetido 32 veces).
public static class DemoAdjusters
{
    public const int Count = 5;

    public static IEnumerable<(Guid Id, string DisplayName)> All => Enumerable.Range(0, Count)
        .Select(index => (Guid.Parse(new string((char)('1' + index), 32)), $"Ajustador {index + 1:00}"));
}
