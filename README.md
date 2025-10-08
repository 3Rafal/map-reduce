# Production ready(ish) MapReduce

Cloud-native MapReduce proof of concept built with .NET 9 Web APIs. The solution exposes an API service that coordinates mapper and reducer workers communicating over HTTP and persisting artifacts in MinIO (S3-compatible object storage).

## Services

- **API Service** – handles file uploads (`POST /files`), job orchestration (`POST /jobs`), status (`GET /jobs/{id}`), result download (`GET /jobs/{id}/result`), and callbacks from workers.
- **Mapper Service** – performs word count map step, writing intermediate JSON outputs back to MinIO and notifying the API.
- **Reducer Service** – aggregates intermediate results, produces the final JSON artifact, and notifies the API.
- **MinIO** – provides object storage for inputs, intermediates, and final outputs.

## Prerequisites

- .NET 9 SDK (RC or later)
- Docker and Kubernetes tooling (e.g., Docker Desktop with Kubernetes or kind/minikube)
- `kubectl`

## Local Development

1. **Restore and build**
   ```bash
   dotnet restore MapReduceSolution.sln
   dotnet build MapReduceSolution.sln
   ```

2. **Run MinIO locally**
   ```bash
   docker run -p 9000:9000 -p 9001:9001 \
     -e MINIO_ROOT_USER=minioadmin \
     -e MINIO_ROOT_PASSWORD=minioadmin \
     minio/minio server /data --console-address :9001
   ```

3. **Run the services (separate shells)**
   ```bash
   dotnet run --project src/ApiService
   dotnet run --project src/MapperService --urls http://localhost:5072
   dotnet run --project src/ReducerService --urls http://localhost:5082
   ```

   Adjust the coordinator URLs in `src/ApiService/appsettings.json` if you change ports.

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
2. Apply manifests:
   ```bash
   kubectl apply -f deploy/k8s/minio.yaml
   kubectl apply -f deploy/k8s/mapper-service.yaml
   kubectl apply -f deploy/k8s/reducer-service.yaml
   kubectl apply -f deploy/k8s/api-service.yaml
   kubectl apply -f deploy/k8s/ingress.yaml
   ```
3. Access the API via the ingress at `http://localhost/api`.

## Testing

- Requires Docker daemon for MinIO.
- Run end-to-end suite:
  ```bash
  dotnet test tests/EndToEndTests/EndToEndTests.csproj
  ```

## Next Steps

- Introduce persistent metadata storage (e.g., PostgreSQL) instead of the in-memory job registry.
- Add asynchronous orchestration via message queues.
- Extend observability with OpenTelemetry and Prometheus metrics.
- Harden authentication and authorization (e.g., JWT).
