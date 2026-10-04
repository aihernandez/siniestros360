// Cada clase levanta un servicio completo (host, bus, outbox) sobre el mismo PostgreSQL: se ejecutan en serie.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
