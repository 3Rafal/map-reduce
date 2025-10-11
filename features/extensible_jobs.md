# Extensible jobs
  - Abstract the pipeline
    Define interfaces for mapper and reducer logic (IMapperTask, IReducerTask) plus a descriptor model (job type id, required parameters). The existing WordCountMapper/Reducer become one implementation
    pair.
  - Job descriptors & registry
    Store metadata in a DB table or registry (JobType with mapper/reducer class identifiers, expected payload schema). The API service uses that to build requests and to know which MinIO paths to use.
  - Factory or DI keyed services
    Introduce a mapper/reducer factory keyed by job type (IMapperTaskFactory.Create(jobType)), registering implementations via DI (services.AddSingleton<IMapperTask, WordCountMapper>("wordcount")). That
    lets you add new tasks without touching the coordinator.
  - Parameterizable payloads
    Extend your DTOs to carry job-specific settings (JobDefinitionId, Dictionary<string,string> or strongly-typed options). Map/reduce services parse those to configure their logic.
  - Contracts & schema validation
    Publish Protobuf/OpenAPI contracts for the job descriptor payloads so new job types can validate inputs and intermediate outputs.
  - Plugin packaging
    Each job type ships as a separate assembly or container image; the mapper/reducer services load plugins dynamically (using reflection, AssemblyLoadContext, or gRPC calls to specialized workers).
  - Routing by queue/topic
    When you adopt a message queue, use job-type-specific topics so map/reduce workers subscribe only to the types they support. That keeps new job types decoupled.