# Queue
  - Use RabbitMQ
  - Define contract & channels – create message DTOs for map and reduce tasks. For a simple pipeline:
      - map-jobs topic/queue: API publishes jobs (job id, input location, options).
      - map-results queue: mapper publishes completion events.
      - reduce-jobs queue: API publishes reduce requests when all maps finish.
      - reduce-results queue: reducer publishes final completions.
  - Publish from the API – when /jobs receives a request, persist the job state in a database, then enqueue a map message instead of calling mapper HTTP directly. Use an async client (e.g.
    Confluent.Kafka, MassTransit with RabbitMQ) registered via DI.
  - Consume in workers – mapper and reducer services run background consumers (hosted services). The consumer pulls messages, processes them, and publishes results back to the appropriate queue. Keep
    their WebAPI surface only for health/diagnostics.
  - Handle acknowledgments & retries – configure the broker’s ack/retry/dead-letter features. Deserialize message payloads using a stable schema (Protobuf or JSON with versioning). Make mapper/reducer
    idempotent since messages may be delivered more than once.
  - Track state asynchronously – the API listens on the result queues (or exposes a webhook endpoint) to update job status in the DB. Clients poll /jobs/{id} as before, but now the state transitions are
    driven by messages, not HTTP callbacks.
  - Scale – once the components are decoupled:
      - increase mapper/reducer replicas to pull more from the queue,
      - tune prefetch/consumer concurrency,
      - use queue depth as an HPA metric.