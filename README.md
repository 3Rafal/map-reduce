# MapReduce with features

Cloud-native MapReduce proof of concept

## Services

- **API Service** – handles file uploads (`POST /files`), job orchestration (`POST /jobs`), status (`GET /jobs/{id}`), result download (`GET /jobs/{id}/result`), and callbacks from workers.
- **Mapper Service** – performs word count map step, writing intermediate JSON outputs back to MinIO and notifying the API.
- **Reducer Service** – aggregates intermediate results, produces the final JSON artifact, and notifies the API.
- **MinIO** – provides object storage for inputs, intermediates, and final outputs.

## Prerequisites

- .NET 9 SDK
- Docker and Kubernetes tooling
- RabbitMQ (for local development)

## Local Development

1. **Restore and build**
   ```bash
   dotnet restore MapReduceSolution.sln
   dotnet build MapReduceSolution.sln
   ```

2. **Run dependencies locally**
   ```bash
   # Start MinIO
   docker run -p 9000:9000 -p 9001:9001 \
     -e MINIO_ROOT_USER=minioadmin \
     -e MINIO_ROOT_PASSWORD=minioadmin \
     minio/minio server /data --console-address :9001

   # Start RabbitMQ
   docker run -p 5672:5672 -p 15672:15672 \
     -e RABBITMQ_DEFAULT_USER=guest \
     -e RABBITMQ_DEFAULT_PASS=guest \
     rabbitmq:3.13-management
   ```

3. **Run the services (separate shells)**
   ```bash
   dotnet run --project src/ApiService
   dotnet run --project src/MapperService --urls http://localhost:5072
   dotnet run --project src/ReducerService --urls http://localhost:5082
   ```

   All services now communicate via RabbitMQ queues instead of HTTP callbacks.

4. **Execute the vertical slice**
   ```bash
   # Upload input
   curl -F "file=@sample.txt" http://localhost:5000/files

   # Submit job (use the bucket/objectKey from upload response)
   curl -H "Content-Type: application/json" \
     -d '{"inputFile":{"bucketName":"mapreduce","objectKey":"inputs/..."}}' \
     http://localhost:5000/jobs

   # Poll job status
   curl http://localhost:5000/jobs/{jobId}

   # Retrieve results once status is Completed
   curl http://localhost:5000/jobs/{jobId}/result
   ```

## Container Images

Each service has a multi-stage Dockerfile in its project directory:

```bash
docker build -t mapreduce-api:latest src/ApiService
docker build -t mapreduce-mapper:latest src/MapperService
docker build -t mapreduce-reducer:latest src/ReducerService
```

## Kubernetes Deployment

1. Build/push the images to a registry accessible by your cluster (or use `imagePullPolicy: Never` with local clusters).
2. Apply manifests in order:
   ```bash
   kubectl apply -f deploy/k8s/minio.yaml
   kubectl apply -f deploy/k8s/rabbitmq.yaml
   kubectl apply -f deploy/k8s/mapper-service.yaml
   kubectl apply -f deploy/k8s/reducer-service.yaml
   kubectl apply -f deploy/k8s/api-service.yaml
   kubectl apply -f deploy/k8s/ingress.yaml
   ```
3. Access the API via the ingress at `http://localhost/api`.
4. RabbitMQ management UI is available at `http://localhost:15672` (username/password: guest/guest).

## Testing

- Requires Docker daemon for MinIO.
- Run end-to-end suite:
  ```bash
  dotnet test tests/EndToEndTests/EndToEndTests.csproj
  ```
- Run Kubernetes end-to-end (requires kubectl, active cluster, ingress/port-forward permissions):
  ```bash
  dotnet test tests/EndToEndTests/EndToEndTests.csproj --filter FullyQualifiedName~Kubernetes
  ```

## Queue-Based Architecture

This implementation now uses RabbitMQ for asynchronous communication between services:

- **Queue flows**:
  - API publishes `MapJobMessage` to `map-jobs` queue
  - Mapper consumes map jobs, processes files, publishes `MapResultMessage` to `map-results` queue
  - API consumes map results, publishes `ReduceJobMessage` to `reduce-jobs` queue
  - Reducer consumes reduce jobs, aggregates results, publishes `ReduceResultMessage` to `reduce-results` queue
  - API consumes final results and updates job status

- **Benefits**:
  - Decoupled services - no direct HTTP dependencies
  - Better resilience - queues buffer requests
  - Scalability - multiple mapper/reducer instances can process jobs concurrently
  - Retry and error handling through MassTransit

## Next Steps

- Introduce persistent metadata storage (e.g., PostgreSQL) instead of the in-memory job registry.
- Extend observability with OpenTelemetry and Prometheus metrics.
- Harden authentication and authorization (e.g., JWT).
- Configure MassTransit retry policies and dead-letter queues for better error handling.

## Scale-Out Ideas

- Container autoscaling: add HPA rules in the K8s manifests (CPU/queue depth driven) and ensure stateless workers read config from env/ConfigMaps so Pods can scale horizontally.
- Shared state & metadata: replace the in-memory job dictionary with a durable store (PostgreSQL, Redis) so you can run multiple API pods behind a load balancer without losing coordination data.
- Sharding large jobs: extend the mapper to partition input files and fan out tasks; reducers can aggregate per-shard results using a combiner stage to avoid bottlenecks.
- Back-pressure & retries: configure MassTransit retry policies, circuit breakers, and dead-letter queues for failing tasks.
