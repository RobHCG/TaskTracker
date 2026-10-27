# TaskTracker

Two small services, built with ASP.NET Core 10, EF Core and RabbitMQ:

- **Tasks**: a REST API for managing tasks. When a task is completed, it records a `TaskCompleted` event in an outbox, and a background worker publishes it.
- **Notifications**: consumes `TaskCompleted` and records one notification per completion, ignoring duplicate deliveries.

The services never call each other. They only meet through RabbitMQ.

## Tasks endpoints

| Method | Route | Success | Errors |
|---|---|---|---|
| `GET` | `/api/tasks` | 200 | |
| `GET` | `/api/tasks/{id}` | 200 | 404 |
| `POST` | `/api/tasks` | 201 + `Location` header | 400 |
| `PUT` | `/api/tasks/{id}` | 200 | 400, 404 |
| `DELETE` | `/api/tasks/{id}` | 204 | 404 |

Errors are returned as [ProblemDetails](https://www.rfc-editor.org/rfc/rfc9457) (`application/problem+json`).

## Running it

### With Docker Compose (recommended)

**Prerequisite:** [Docker Desktop](https://www.docker.com/products/docker-desktop/), running.

```bash
docker compose up --build
```

This starts three containers:

| Service | Address | Notes |
|---|---|---|
| Tasks | `http://localhost:5080` | Applies database migrations on startup. Data is kept in the `tasks-data` volume. |
| Notifications | `http://localhost:5090` | Applies database migrations on startup, and waits for RabbitMQ to pass its healthcheck before starting. Data is kept in the `notifications-data` volume. |
| RabbitMQ | dashboard at `http://localhost:15672` | Log in as `tasks` / `tasks` (local development only). Messages are kept in the `rabbitmq-data` volume. |

`docker compose down` stops and removes the containers. The volumes, and your data, are kept.

### Locally with the .NET SDK

**Prerequisites:** the [.NET 10 SDK](https://dotnet.microsoft.com/download) and the EF Core CLI tool:

```bash
dotnet tool install --global dotnet-ef --version 10.0.12
```

Build the solution:

```bash
dotnet build
```

Create the local SQLite database:

```bash
dotnet ef database update --project services/tasks/Tasks.Api
```

Start the API (it listens on `http://localhost:5019`):

```bash
dotnet run --project services/tasks/Tasks.Api --launch-profile http
```

Completed tasks' events are published to RabbitMQ on `localhost:5672`. The simplest way to run it is to start just the broker from the Compose file:

```bash
docker compose up rabbitmq
```

Without it, everything still works: events wait in the outbox (logging a warning every second) and are sent once the broker is reachable.

Notifications runs the same way (it listens on `http://localhost:5087`):

```bash
dotnet ef database update --project services/notifications/Notifications.Api
```

```bash
dotnet run --project services/notifications/Notifications.Api --launch-profile http
```

Unlike Tasks, Notifications connects to RabbitMQ as soon as it starts, so the broker must already be running. If it can't connect, the app stops.

### Configuration

| Setting | Default | Purpose |
|---|---|---|
| `ConnectionStrings:TasksDb` | `Data Source=tasks.db` | SQLite database file. Compose sets `/app/data/tasks.db`, inside the volume. |
| `ConnectionStrings:NotificationsDb` | `Data Source=notifications.db` | The Notifications database file. Compose sets `/app/data/notifications.db`. |
| `Database:MigrateOnStartup` | `false` | Both services: apply EF Core migrations when the app starts. Compose turns this on; in production, migrations would run as a separate deployment step. |
| `RabbitMq:HostName` | `localhost` | Both services. Compose sets `rabbitmq`, the broker's service name. |
| `RabbitMq:Port` | `5672` | |
| `RabbitMq:UserName` / `RabbitMq:Password` | `tasks` / `tasks` | Local development only. |

As environment variables, replace `:` with `__`, for example `Database__MigrateOnStartup=true`.

## Using the API

All examples use the Docker Compose port, `http://localhost:5080`. If you're running the Tasks service with `dotnet run` instead, use port 5019. Responses below are real output, formatted for readability. More example requests are in [`Tasks.Api.http`](services/tasks/Tasks.Api/Tasks.Api.http), which is set up for `dotnet run` (port 5019).

The `curl` examples are written for **bash** (macOS, Linux, or Git Bash on Windows). In **PowerShell**, the one-line examples work as they are (in Windows PowerShell 5.1, type `curl.exe` instead of `curl`). The examples that send JSON have a PowerShell version underneath them.

### Create a task

```bash
curl -i -X POST http://localhost:5080/api/tasks \
  -H "Content-Type: application/json" \
  -d '{"title":"Write the README","description":"Cover setup, endpoints and design decisions"}'
```

In PowerShell:

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5080/api/tasks -ContentType 'application/json' -Body '{"title":"Write the README","description":"Cover setup, endpoints and design decisions"}' | ConvertTo-Json
```

`201 Created`, with a `Location` header pointing at the new task:

```http
Location: http://localhost:5080/api/tasks/1
```
```json
{
  "id": 1,
  "title": "Write the README",
  "description": "Cover setup, endpoints and design decisions",
  "isComplete": false,
  "createdAt": "2026-10-09T14:01:35.3169515Z"
}
```

`title` is required (max 200 characters). `description` is optional (max 2000).

### List all tasks

```bash
curl http://localhost:5080/api/tasks
```

`200 OK`:

```json
[
  {
    "id": 1,
    "title": "Write the README",
    "description": "Cover setup, endpoints and design decisions",
    "isComplete": false,
    "createdAt": "2026-10-09T14:01:35.3169515Z"
  }
]
```

### Get one task

```bash
curl http://localhost:5080/api/tasks/1
```

`200 OK` with the task, or `404 Not Found` if it doesn't exist.

### Update a task

`PUT` replaces the task's editable fields, so send all of them:

```bash
curl -X PUT http://localhost:5080/api/tasks/1 \
  -H "Content-Type: application/json" \
  -d '{"title":"Write the README","description":"Cover setup, endpoints and design decisions","isComplete":true}'
```

In PowerShell:

```powershell
Invoke-RestMethod -Method Put -Uri http://localhost:5080/api/tasks/1 -ContentType 'application/json' -Body '{"title":"Write the README","description":"Cover setup, endpoints and design decisions","isComplete":true}' | ConvertTo-Json
```

`200 OK`:

```json
{
  "id": 1,
  "title": "Write the README",
  "description": "Cover setup, endpoints and design decisions",
  "isComplete": true,
  "createdAt": "2026-10-09T14:01:35.3169515Z"
}
```

When this changes a task from not complete to complete, the API records a `TaskCompleted` event, which is published within about a second (see [Events](#events)).

### Delete a task

```bash
curl -i -X DELETE http://localhost:5080/api/tasks/1
```

`204 No Content`, with an empty body.

### Errors

A task that doesn't exist returns `404 Not Found`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.5",
  "title": "Not Found",
  "status": 404,
  "traceId": "00-673fcd438d05f4bc2051ff84b6a94062-f560224d8105ab71-00"
}
```

Invalid input returns `400 Bad Request`, listing each problem by field:

```bash
curl -X POST http://localhost:5080/api/tasks \
  -H "Content-Type: application/json" \
  -d '{"title":""}'
```

In PowerShell 7 (`-SkipHttpErrorCheck` shows the error body instead of throwing an exception):

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5080/api/tasks -ContentType 'application/json' -Body '{"title":""}' -SkipHttpErrorCheck | ConvertTo-Json
```
```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "Title": ["The Title field is required."]
  },
  "traceId": "00-6fc297b67e561abfa810ecb6e1e3d6f6-431afc4718f21aac-00"
}
```

## Events

An **event** records something that has happened. There's one so far, `TaskCompleted`. On its way through RabbitMQ, an event travels as a **message**: the event as a JSON body, plus RabbitMQ properties such as `message_id`.

When a task changes from not complete to complete, the Tasks service publishes `TaskCompleted` to the durable topic exchange `tasks` with routing key `task.completed`. It does not publish when an already-complete task is updated or when a task is reopened. Completing a reopened task again publishes a new event with a new `eventId`.

A real message, as shown in the RabbitMQ dashboard:

```json
{
  "eventId": "8bd95168-5139-4a6e-8574-6c3d064d9d64",
  "taskId": 3,
  "title": "Caught by the queue",
  "completedAt": "2026-10-13T13:02:37.5199989Z"
}
```

Message properties: `message_id` is the `eventId`, `content_type` is `application/json`, and `delivery_mode` is `2` (persistent).

The Tasks service creates the exchange but no queues. Each consumer creates and binds its own queue, so a message published while no queue is bound is dropped.

### The outbox

The Tasks service never publishes during a request. When a task is completed, it saves the task and the `TaskCompleted` event to the outbox (the `OutboxMessages` table) in the **same** database save, so either both are saved or neither is. A background worker in the Tasks service checks the outbox every second, publishes each unsent event (oldest first), and records when it was sent (`SentAt`).

If RabbitMQ is down, the request still succeeds and the event waits in the outbox. The worker logs a warning on each attempt and sends the event once the broker is back. Checked by stopping RabbitMQ in Compose: a task completed during the outage was notified about two minutes later, when the broker returned, with its original `completedAt`.

An event can occasionally be published twice (for example, if the app stops between publishing it and saving `SentAt`). It carries the same `eventId` both times, so Notifications ignores the duplicate.

Notifications creates:

| Queue | Bound to | Purpose |
|---|---|---|
| `notifications.task-completed` | `tasks`, with `task.completed` | Durable. Messages wait here while Notifications is down. |
| `notifications.task-completed.dead-letter` | (dead-letter target) | Messages that Notifications can't process are moved here instead of being retried forever, so they can be inspected and replayed. |

## Notifications

`GET /api/notifications` lists recorded notifications, newest first. On port 5090 with Compose:

```bash
curl http://localhost:5090/api/notifications
```

`200 OK`, for a task that was completed in the Tasks service about 230 ms earlier:

```json
[
  {
    "eventId": "84b95e9c-666d-442f-8a86-1426b15bfa13",
    "taskId": 5,
    "taskTitle": "Notify me",
    "completedAt": "2026-10-15T15:08:08.2190454Z",
    "notifiedAt": "2026-10-15T15:08:08.4490414Z"
  }
]
```

How each message is handled:

| Outcome | What happens |
|---|---|
| A new `eventId` | A notification is saved, then the message is acknowledged |
| An `eventId` already recorded (a duplicate delivery) | Nothing is saved, and the message is acknowledged |
| The message can't be processed (bad JSON, a database error) | It's rejected without requeue, so RabbitMQ moves it to the dead-letter queue |
| The app is shutting down mid-message | Nothing; RabbitMQ redelivers the unacknowledged message later |

## Running the tests

```bash
dotnet test
```

No database, broker or running API is needed for most tests (29 of 34): they create their own in-memory databases and use a fake event publisher, or leave the consumer out. Two categories need more:

| Category | Tests | Needs |
|---|---|---|
| `Integration` | 4 | Docker running. They start their own throwaway RabbitMQ container. |
| `EndToEnd` | 1 | The whole system running with `docker compose up`. |

To run only the tests that need nothing:

```bash
dotnet test --filter "TestCategory!=Integration&TestCategory!=EndToEnd"
```

### The contract test

The `TaskCompleted` event is covered by a consumer-driven contract test ([PactNet](https://github.com/pact-foundation/pact-net)):

1. **Notifications** (the consumer) describes the message it needs, and proves it can handle it. This writes the contract to [`pacts/Notifications-Tasks.json`](pacts/Notifications-Tasks.json).
2. **Tasks** (the provider) builds its real event with its real JSON settings, and checks it against that file.

The contract only lists what Notifications uses, so the Tasks service can add fields freely, but renaming or removing one that Notifications relies on (or changing how names are written, such as switching to snake_case) fails its build.

The contract file is kept in the repo. If Notifications' expectations change, run its tests first so the file is regenerated, then the Tasks service's.

### The end-to-end test

`EndToEnd.Tests` checks the whole flow through the running containers. It creates a task in the Tasks service, completes it, then polls `GET /api/notifications` until the matching notification appears. It only talks HTTP and references neither service's code, like any other client.

With the containers running (see [With Docker Compose](#with-docker-compose-recommended)):

```bash
dotnet test EndToEnd.Tests
```

- **It polls instead of sleeping.** The notification arrives within about a second of the task being completed, so the test checks every 250 ms and stops as soon as it's there, giving up after 10 seconds. A fixed sleep would be slower when things are fast and flaky when they're slow.
- **Each run uses a unique title** (containing a GUID), because the Compose volumes keep data from earlier runs.
- **The addresses are configurable.** `TASKS_API_URL` and `NOTIFICATIONS_API_URL` default to `http://localhost:5080` and `http://localhost:5090`.

How it fails, checked by breaking the system on purpose:

| What's wrong | Result |
|---|---|
| Notifications is stopped | Fails at once: the connection to port 5090 is refused. The event waits in the queue and is recorded when Notifications starts again. |
| Notifications is running, but its queue isn't bound to the exchange | Fails after 10 seconds with `No notification for 'End-to-end …' within 10s`. This event is never recorded, even after the binding is restored, because nothing was there to hold it. |

## Project structure

```
services/
  tasks/
    Tasks.Api/
      Controllers/   HTTP only: routing and status codes
      Services/      Business logic (ITaskService, TaskService)
      Data/          EF Core DbContext and the UTC converter
      Models/        Entities (TaskItem, OutboxMessage)
      Contracts/     Request DTOs and validation rules
      Events/        TaskCompleted, IEventPublisher and the RabbitMQ publisher
      Outbox/        OutboxDispatcher (one round of sending) and the background service that runs it every second
      Migrations/    EF Core migrations
      Dockerfile
    Tasks.Tests/
      TaskServiceTests.cs              Unit tests against in-memory SQLite
      OutboxDispatcherTests.cs         Sending, marking sent, and retrying after a failed publish
      TasksApiTests.cs                 API integration tests via WebApplicationFactory
      RabbitMqEventPublisherTests.cs   Publisher test against a real RabbitMQ (Testcontainers)
      TaskCompletedContractTests.cs    Provider side of the contract test (Pact)
      FakeEventPublisher.cs            Records published events, or fails on demand
  notifications/
    Notifications.Api/
      Controllers/   GET /api/notifications
      Services/      NotificationRecorder (writes, exactly once) and NotificationReader (reads)
      Messaging/     TaskCompletedConsumer (the RabbitMQ background service) and its settings
      Messages/      Notifications' own copy of the TaskCompleted shape
      Contracts/     The response DTO
      Data/          EF Core DbContext, entity, and the UTC converter
      Migrations/    EF Core migrations
      Dockerfile
    Notifications.Tests/
      NotificationRecorderTests.cs     The idempotency rule, against in-memory SQLite
      TaskCompletedConsumerTests.cs    The real consumer against a real RabbitMQ (Testcontainers)
      NotificationsApiTests.cs         API integration tests, with the consumer removed
      TaskCompletedContractTests.cs    Consumer side of the contract test; writes the pact file
EndToEnd.Tests/
  TaskCompletionFlowTests.cs       The whole flow through the running containers, over HTTP
pacts/
  Notifications-Tasks.json         The contract between the two services
docker-compose.yml
TaskTracker.slnx
```

## Design decisions

- **Thin controllers, logic in a service.** Controllers only deal with HTTP and map results to status codes. The service holds the logic, which keeps it testable without a web server.
- **Request DTOs instead of binding to the entity.** Callers can only send the fields they're allowed to set, so they can't supply their own `Id` or `CreatedAt`.
- **Validation at the boundary.** Rules live on the request DTOs as DataAnnotations, and `[ApiController]` rejects invalid input with a 400 before any of my code runs.
- **Consistent errors that don't leak.** Unexpected exceptions become a ProblemDetails 500 without internal details. This is covered by a test.
- **`TimeProvider` instead of `DateTime.UtcNow`.** The clock is injected, so tests can freeze time and assert exact values.
- **Timestamps are always UTC.** SQLite stores dates without a time zone, so in both services a value converter, applied by convention to every `DateTime`, re-marks values as UTC when they're read back. New date columns can't forget, and tests cover it.
- **SQLite.** Zero setup for anyone cloning the repo. The connection string lives in configuration, so tests swap in their own database without any code changes.
- **Events go through an interface.** `TaskService` depends on `IEventPublisher`, not on RabbitMQ, so tests use a fake and the broker could be swapped by changing one class.
- **A transactional outbox.** The event is saved in the same transaction as the task and published later by a background worker, so a completed task's event can't be lost if RabbitMQ is down, and the API never announces a change that didn't save.
- **Producers own exchanges, consumers own queues.** The Tasks service doesn't know who is listening, so adding a consumer needs no change here.
- **A least-privilege container.** The app runs as a non-root user and can only write to its data folder, not to its own code.
- **An idempotent consumer, in two layers.** Notifications first checks whether the `eventId` is already recorded. A unique index on `EventId` is the real guarantee: if two copies of a message arrive at the same moment, the second insert fails, and the consumer treats that specific failure as "already handled".
- **Acknowledge only after saving.** A crash between the two means RabbitMQ redelivers the message, which is exactly why duplicates must be handled.
- **A dead-letter queue instead of endless retries.** A message that can't be processed would otherwise loop forever.
- **One DI scope per message.** The consumer is a long-lived background service, so it creates a fresh scope (and DbContext) for each message, as ASP.NET does for each HTTP request.
- **No shared event class between the services.** Each defines the shape it uses, so they can change independently. A consumer-driven contract test keeps them in step, using each service's real JSON settings.
- **Writes and reads kept apart.** `NotificationRecorder` owns the idempotency rules, and `NotificationReader` owns ordering and the response shape.
- **Notifications waits for RabbitMQ to be healthy.** Compose's healthcheck means "ready", not just "started".

## Testing approach

There are three levels in each service, because each one catches bugs the others can't:

- **Unit tests** call the service classes directly and check the logic in detail. They're fast and point straight at the failing method. They use a real SQLite database in memory rather than EF's InMemory provider, which doesn't behave like a real database. They clear EF's change tracker before reading data back, so they prove data was saved rather than just held in memory.
- **Outbox tests** run the dispatcher against in-memory SQLite with a fake publisher that can be switched to failing. They prove a message is sent and marked sent, isn't sent twice, and survives a failed publish to go out on the next round.
- **API integration tests** run the whole app in memory with `WebApplicationFactory`, with RabbitMQ faked, and check what a caller sees: routing, validation, status codes, DI wiring and error handling. None of that runs in the unit tests.
- **A broker integration test** publishes through the real RabbitMQ publisher to a throwaway RabbitMQ container (Testcontainers) and reads the message back, checking the exchange, routing key, payload and properties. It's the only test that doesn't use the fake publisher.

Notifications follows the same pattern:

- **Unit tests** pin the idempotency rule: a new event is saved, the same event twice gives one notification, a new event for the same task gives a second one, and the database itself rejects a duplicate `EventId`.
- **Broker integration tests** start the real app against a throwaway RabbitMQ and publish hand-written JSON, as the Tasks service would. To prove a duplicate was ignored without waiting a fixed time, the test publishes a different "marker" event last: with one consumer processing in order, once the marker is recorded, both copies of the duplicate have been handled.
- **API integration tests** start the app with the consumer removed, so they never touch a broker.

Between the two services, a **contract test** checks the one thing neither service's own tests can see: that the message the Tasks service sends is the message Notifications needs. Its other tests stay green if it renames a field, because it's still consistent with itself; only the contract test notices.

Over the whole system, a single **end-to-end test** runs the real flow through the containers. It's deliberately just one test: the levels below already cover the details, so this one only proves the pieces are wired together (the Compose file, ports, the exchange and the queue binding), which no other test can.

## What I'd do next

- Return response DTOs instead of the `TaskItem` entity, so the API contract can't change by accident when the database model does
- Pagination on `GET /api/tasks`
- Authentication
- Delete sent events from the outbox after a while, so the table doesn't grow forever
- Lock events in the outbox while sending them, before running several Tasks service instances, so two workers don't send the same event (Notifications would ignore the duplicate, but it's wasted work)
- Quieten EF's query logging: the outbox check logs a query every second
- Retries with back-off for temporary failures, before a message is dead-lettered
- A separate RabbitMQ user per service, and named connections so the dashboard shows which service is which
- A Pact Broker, if the services move to separate repos or teams
- A GitHub Actions pipeline to build and test on every push, starting the containers before the end-to-end test
