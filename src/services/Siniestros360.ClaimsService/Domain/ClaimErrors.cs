using Siniestros360.SharedKernel;

namespace Siniestros360.ClaimsService.Domain;

// Catálogo de errores del expediente. El código es estable (clientes y pruebas); la descripción la ve el usuario.
public static class ClaimErrors
{
    // También cuando el siniestro existe pero el usuario no participa en él: no se revela su existencia.
    public static Error NotFound(Guid claimId) => Error.NotFound("Claims.NotFound", $"No se encontró el siniestro {claimId}.");

    public static readonly Error AlreadyFinished = Error.Conflict("Claims.AlreadyFinished", "Un siniestro cerrado o cancelado ya no puede cambiar.");

    public static readonly Error ArrivalRequiresAssignment = Error.Conflict("Claims.ArrivalRequiresAssignment", "Sólo se puede registrar la llegada a un siniestro asignado.");

    public static readonly Error StartRequiresArrival = Error.Conflict("Claims.StartRequiresArrival", "La atención sólo puede iniciar después de registrar la llegada.");

    public static readonly Error CompleteRequiresStart = Error.Conflict("Claims.CompleteRequiresStart", "El servicio sólo puede finalizar después de iniciar la atención.");

    public static readonly Error NotAssignedAdjuster = Error.Forbidden("Claims.NotAssignedAdjuster", "El ajustador no está asignado a este siniestro.");

    // Un ajustador sin adjuster_id en el token no puede actuar sobre ningún siniestro.
    public static readonly Error AdjusterIdMissing = Error.Forbidden("Claims.AdjusterIdMissing", "El token no identifica a un ajustador.");
}
