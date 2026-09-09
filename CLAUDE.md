# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What this is

A learning-focused ASP.NET Core 8 Web API demonstrating **passwordless** access to
Azure services. The hard rule: **no secrets, keys, or connection strings** in code
or configuration. Configuration holds only endpoint URLs and the storage account
name. Keep code readable over clever.

## Commands

```bash
dotnet build IdentityLab.slnx                 # build everything
dotnet run --project IdentityLab.Api          # run (uses launchSettings.json ports)
dotnet run --project IdentityLab.Api --no-launch-profile   # run on ASPNETCORE_URLS instead
```

There is no test project yet. If you add one, wire it into `IdentityLab.slnx`
(`dotnet sln IdentityLab.slnx add <path>`); `dotnet test` then runs it, and
`dotnet test --filter "FullyQualifiedName~<Name>"` runs a single test.

The solution file is the new XML `.slnx` format — needs SDK 9.0.200+ (10.x is
installed here). Only `net8.0` is targeted; the .NET 8 runtime is present.

## Architecture

**One credential, shared.** `Program.cs` creates a single
`DefaultAzureCredential` and passes that same instance to every Azure client so
its token cache is actually reused. It resolves to the app's managed identity in
Azure and to `az login` / Visual Studio locally — identical code both places.
Never construct a second credential, and never add a key- or connection-string-based
overload.

**Configuration flow.** App Configuration is added as an `IConfiguration` source
in `Program.cs` *before* the container is built. Key Vault secrets are **not**
read with a `SecretClient` — they live in App Configuration as Key Vault
references and are resolved at startup by `ConfigureKeyVault(...)` using the same
credential. So `ConfigController` just reads `IConfiguration` keys (`Demo:Message`,
`Demo:SecretValue`). App Configuration is registered with `optional: true` so the
app still boots (and `/health` works) when the endpoint is unset; `ConfigController`
returns 503 in that case.

**Clients vs. services.** `BlobServiceClient` and `EventGridPublisherClient` are
registered as **singletons** via factory lambdas that read endpoints from config
and throw `ConfigurationMissingException` if a required value is missing. These
throw lazily on first resolve, not at startup (so `/health` and `/api/config/demo`
work with nothing configured); `ProblemExceptionFilter` in `Infrastructure/` maps
that exception to a 503 ProblemDetails. The interface-backed `BlobStorageService`
/ `EventGridPublisher` are the only things controllers depend on — keep Azure SDK
types out of controllers.

**Download URLs are user delegation SAS.** `BlobStorageService.CreateDownloadUrlAsync`
calls `GetUserDelegationKeyAsync` and signs a `BlobSasBuilder` with that key.
Do not introduce `StorageSharedKeyCredential` or account-key signing — that
defeats the entire point of the project. This is also why the managed identity
needs **both** `Storage Blob Data Contributor` (data) and `Storage Blob Data
Delegator` (the delegation key) — see the role table in `README.md`.

**Error handling contract.** Public service methods are async and take a
`CancellationToken`. Controllers catch `Azure.RequestFailedException`, map
`Status == 403` to an HTTP 403 (missing role), `Status == 0` to 502, and
otherwise pass the status through — and **log the service name and the specific
role to check**, because a wrong RBAC assignment is the most common failure. A
failed Event Grid publish after a successful upload is logged but does **not**
fail the request (the file is already stored).

## Conventions

- New Azure integrations follow the same shape: an `I…`/`…` interface pair in
  `Services/`, the SDK client as a singleton in `Program.cs`, the shared
  `credential`, `RequestFailedException` handling with a role hint in the log.
- `appsettings.json` uses `//` comments (the .NET JSON config provider allows
  them). Every new setting gets a one-line comment. Endpoint URLs and the storage
  account name are the only Azure values allowed in any config file.
- Response bodies are `record` types in `Models/`.
- When you add a role requirement, update the role-assignment table **and** both
  `az` loops in `README.md` (managed identity + local developer).
