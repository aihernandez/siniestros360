using MassTransit;
using Siniestros360.Contracts.Events;
using Siniestros360.DispatchService.Domain;

namespace Siniestros360.DispatchService.Application;

// Saga de asignación (orquestación) con MassTransit. Una instancia por siniestro, correlacionada por ClaimId.
//
//   Initial ──ClaimReported──► Assigned ──AdjusterArrived──► OnSite
//      │                         │  ▲
//      └─sin candidatos─► Unavailable ──RetryAssignment / asignación manual──┘
//   Assigned ──SLA de llegada vencido o reasignación manual──► Assigned (otro ajustador; se libera el anterior)
//   cualquier estado activo ──ClaimCancelled / ClaimClosed──► Cancelled / Closed (se libera el ajustador)
//
// Compensación: liberar la reserva del ajustador al reasignar, cancelar o cerrar. Todo cambio de estado, reserva y
// evento publicado se confirma en una sola transacción (repositorio EF + consumer outbox sobre el mismo DbContext).
public sealed class AssignmentStateMachine : MassTransitStateMachine<AssignmentState>
{
    public State Assigned { get; private set; } = null!;
    public State Unavailable { get; private set; } = null!;
    public State OnSite { get; private set; } = null!;
    public State Closed { get; private set; } = null!;
    public State Cancelled { get; private set; } = null!;

    public Event<ClaimReported> ClaimReported { get; private set; } = null!;
    public Event<RetryAssignment> RetryRequested { get; private set; } = null!;
    public Event<ManualAssignmentRequested> ManualAssignment { get; private set; } = null!;
    public Event<ManualReassignmentRequested> ManualReassignment { get; private set; } = null!;
    public Event<SlaBreached> SlaBreached { get; private set; } = null!;
    public Event<AdjusterArrived> AdjusterArrived { get; private set; } = null!;
    public Event<ClaimCancelled> ClaimCancelled { get; private set; } = null!;
    public Event<ClaimClosed> ClaimClosed { get; private set; } = null!;

    public AssignmentStateMachine()
    {
        InstanceState(x => x.CurrentState);

        Event(() => ClaimReported, e => e.CorrelateById(context => context.Message.ClaimId));
        Event(() => RetryRequested, e => { e.CorrelateById(context => context.Message.ClaimId); e.OnMissingInstance(m => m.Discard()); });
        Event(() => ManualAssignment, e => { e.CorrelateById(context => context.Message.ClaimId); e.OnMissingInstance(m => m.Discard()); });
        Event(() => ManualReassignment, e => { e.CorrelateById(context => context.Message.ClaimId); e.OnMissingInstance(m => m.Discard()); });
        Event(() => SlaBreached, e => { e.CorrelateById(context => context.Message.ClaimId); e.OnMissingInstance(m => m.Discard()); });
        Event(() => AdjusterArrived, e => { e.CorrelateById(context => context.Message.ClaimId); e.OnMissingInstance(m => m.Discard()); });
        Event(() => ClaimCancelled, e => { e.CorrelateById(context => context.Message.ClaimId); e.OnMissingInstance(m => m.Discard()); });
        Event(() => ClaimClosed, e => { e.CorrelateById(context => context.Message.ClaimId); e.OnMissingInstance(m => m.Discard()); });

        // Un evento que no aplica al estado actual (p. ej. SLA vencido con el ajustador ya en sitio) se ignora.
        OnUnhandledEvent(x => x.Ignore());

        Initially(
            When(ClaimReported)
                .Then(context =>
                {
                    context.Saga.IncidentLatitude = context.Message.Latitude;
                    context.Saga.IncidentLongitude = context.Message.Longitude;
                    context.Saga.StartedAt = context.Message.ReportedAt;
                    context.Saga.UpdatedAt = context.Message.ReportedAt;
                })
                .ThenAsync(context => context.Publish(new AdjusterAssignmentRequested(context.Saga.CorrelationId, context.Saga.IncidentLatitude, context.Saga.IncidentLongitude, context.Message.ReportedAt)))
                .ThenAsync(context => Assign(context))
                .IfElse(context => context.Saga.AdjusterId is not null,
                    assigned => assigned.TransitionTo(Assigned),
                    // Sólo se avisa al entrar en Unavailable; los reintentos posteriores no repiten la alerta.
                    unavailable => unavailable
                        .ThenAsync(context => context.Publish(new NoAdjusterAvailable(context.Saga.CorrelationId, "No hay ajustadores disponibles con GPS reciente.", DateTimeOffset.UtcNow)))
                        .TransitionTo(Unavailable)));

        During(Unavailable,
            When(RetryRequested)
                .ThenAsync(context => Assign(context))
                .If(context => context.Saga.AdjusterId is not null, assigned => assigned.TransitionTo(Assigned)),
            When(ManualAssignment)
                .ThenAsync(context => Assign(context, context.Message.AdjusterId))
                .If(context => context.Saga.AdjusterId is not null, assigned => assigned.TransitionTo(Assigned)));

        // Sólo el SLA de llegada justifica reasignar; con el ajustador en sitio el caso se escala pero no se le quita.
        During(Assigned,
            When(SlaBreached, context => context.Message.SlaType == "WaitingArrival")
                .ThenAsync(context => Reassign(context, "Venció el plazo de llegada.")),
            When(ManualReassignment)
                .ThenAsync(context => Reassign(context, context.Message.Reason)),
            When(AdjusterArrived)
                .Then(context => context.Saga.UpdatedAt = context.Message.ArrivedAt)
                .TransitionTo(OnSite));

        During(Assigned, Unavailable, OnSite,
            When(ClaimCancelled)
                .ThenAsync(context => Finish(context, context.Message.CancelledAt))
                .TransitionTo(Cancelled),
            When(ClaimClosed)
                .ThenAsync(context => Finish(context, context.Message.ClosedAt))
                .TransitionTo(Closed));
    }

    private static async Task Assign<TMessage>(BehaviorContext<AssignmentState, TMessage> context, Guid? preferredAdjusterId = null) where TMessage : class
    {
        var reservation = await Reservations(context).TryReserve(context.Saga, context.CancellationToken, preferredAdjusterId);
        if (reservation is null) return;
        await context.Publish(new AdjusterAssigned(context.Saga.CorrelationId, reservation.AdjusterId, reservation.DistanceKm, context.Saga.UpdatedAt, reservation.EstimatedArrivalAt));
    }

    private static async Task Reassign<TMessage>(BehaviorContext<AssignmentState, TMessage> context, string reason) where TMessage : class
    {
        await context.Publish(new ClaimReassignmentRequested(context.Saga.CorrelationId, reason, DateTimeOffset.UtcNow));
        var previous = context.Saga.AdjusterId;
        var reservation = await Reservations(context).TryReserve(context.Saga, context.CancellationToken, isReassignment: true);
        if (reservation is null)
        {
            // Sin otro ajustador libre, el actual conserva el servicio y la torre recibe la alerta.
            await context.Publish(new NoAdjusterAvailable(context.Saga.CorrelationId, $"No hay otro ajustador disponible para reasignar. {reason}", DateTimeOffset.UtcNow));
            return;
        }

        await context.Publish(new ClaimReassigned(context.Saga.CorrelationId, previous!.Value, reservation.AdjusterId, reason, context.Saga.UpdatedAt));
    }

    private static async Task Finish<TMessage>(BehaviorContext<AssignmentState, TMessage> context, DateTimeOffset at) where TMessage : class
    {
        var reservations = Reservations(context);
        if (context.Saga.AdjusterId is not null) await reservations.Release(context.Saga.AdjusterId.Value, context.Saga.CorrelationId, context.CancellationToken);
        context.Saga.UpdatedAt = at;
        // El ajustador liberado puede atender siniestros que esperaban.
        foreach (var claimId in await reservations.WaitingClaims(context.CancellationToken))
        {
            await context.Publish(new RetryAssignment(claimId));
        }
    }

    private static AdjusterReservations Reservations<TMessage>(BehaviorContext<AssignmentState, TMessage> context) where TMessage : class
        => context.GetServiceOrCreateInstance<AdjusterReservations>();
}
