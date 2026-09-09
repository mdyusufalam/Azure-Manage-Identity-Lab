# IdentityLab

A small ASP.NET Core 8 Web API that talks to four Azure services **without a single
secret or connection string** in code or config. Every call authenticates with
`DefaultAzureCredential` from [`Azure.Identity`](https://learn.microsoft.com/dotnet/api/overview/azure/identity-readme):
the app's **managed identity** when running in Azure, and your **`az login` /
Visual Studio** sign-in when running locally — same code path either way.

The only Azure values in configuration are **endpoint URLs** and a **storage
account name**.

## What it does

| Service | Used for |
| --- | --- |
| Azure App Configuration | Loaded as a configuration source at startup |
| Azure Key Vault | Secrets, referenced *through* App Configuration (resolved at startup) |
| Azure Blob Storage | Upload a file; issue a 15‑minute **user delegation SAS** download link |
| Azure Event Grid | Publish a `FileUploaded` event after each successful upload |

### Endpoints

| Method | Route | Description |
| --- | --- | --- |
| `POST` | `/api/files/upload` | Multipart `file` field → uploads to the blob container, publishes `FileUploaded` (blob name + size) to Event Grid, returns the blob name. |
| `GET` | `/api/files/{blobName}/download-url` | Returns a read‑only **user delegation SAS** URL valid for 15 minutes. Signed with a key from `GetUserDelegationKeyAsync`, **never** an account key. |
| `GET` | `/api/config/demo` | Returns one value from App Configuration and one secret from Key Vault (masked to the first 3 chars) to prove both are wired up. |
| `GET` | `/health` | Liveness check. |

Swagger UI is enabled in Development at `/swagger`.

## Project layout

```
IdentityLab.Api/
  Program.cs                 DI registration + the single DefaultAzureCredential
  Controllers/
    FilesController.cs       upload + download-url
    ConfigController.cs      config/demo
  Services/
    IBlobStorageService.cs / BlobStorageService.cs
    IEventPublisher.cs       / EventGridPublisher.cs
  Infrastructure/
    ConfigurationMissingException.cs
    ProblemExceptionFilter.cs   maps missing-config -> HTTP 503
  Models/FileModels.cs       request/response records
  appsettings.json           placeholder endpoints, one comment per setting
```

`BlobServiceClient` and `EventGridPublisherClient` are registered as **singletons**
(SDK guidance — they're thread‑safe and pooled). The `DefaultAzureCredential` is
created **once** in `Program.cs` and shared by every client so token caching works.

---

## Azure role assignments the managed identity needs

Data‑plane access to these services is **Azure RBAC**, not keys. Assign each role
to the app's managed identity. Scope can be the resource itself (shown here) or a
resource group / subscription if you prefer.

| Service | Role | Scope | Why this role |
| --- | --- | --- | --- |
| App Configuration | **App Configuration Data Reader** | the App Configuration resource | Read key‑values at startup |
| Key Vault | **Key Vault Secrets User** | the Key Vault | Resolve Key Vault references pulled in via App Configuration |
| Blob Storage | **Storage Blob Data Contributor** | the storage account | Create the container and upload blobs |
| Blob Storage | **Storage Blob Data Delegator** | the storage account | Call `GetUserDelegationKey` to sign the SAS — **not** covered by Data Contributor |
| Event Grid | **EventGrid Data Sender** | the Event Grid topic | Publish events to the topic |

> The two separate Storage roles are the classic gotcha: `Storage Blob Data
> Contributor` lets you read/write blob *data*, but issuing a user delegation SAS
> needs the distinct `Storage Blob Data Delegator` role.
>
> If your Key Vault still uses **access policies** instead of RBAC, grant a
> *Get*/*List* **secret** permission to the identity instead of the role above.

### Enable the system‑assigned identity and assign the roles (az CLI)

```bash
# --- names you fill in -------------------------------------------------------
RG=my-rg
APP=my-webapp                 # the App Service hosting this API
APPCONFIG=my-appconfig
KEYVAULT=my-keyvault
STORAGE=mystorageacct
TOPIC=my-eventgrid-topic
# ---------------------------------------------------------------------------

# 1. Turn on the system-assigned managed identity and capture its principal id
PRINCIPAL_ID=$(az webapp identity assign \
  --name "$APP" --resource-group "$RG" \
  --query principalId -o tsv)

# 2. Resolve the target resource ids to use as role-assignment scopes
APPCONFIG_ID=$(az appconfig show   --name "$APPCONFIG" --resource-group "$RG" --query id -o tsv)
KEYVAULT_ID=$(az keyvault show     --name "$KEYVAULT"                        --query id -o tsv)
STORAGE_ID=$(az storage account show --name "$STORAGE" --resource-group "$RG" --query id -o tsv)
TOPIC_ID=$(az eventgrid topic show --name "$TOPIC"   --resource-group "$RG" --query id -o tsv)

# 3. Assign one role per line
for ROLE_SCOPE in \
  "App Configuration Data Reader|$APPCONFIG_ID" \
  "Key Vault Secrets User|$KEYVAULT_ID" \
  "Storage Blob Data Contributor|$STORAGE_ID" \
  "Storage Blob Data Delegator|$STORAGE_ID" \
  "EventGrid Data Sender|$TOPIC_ID"
do
  ROLE="${ROLE_SCOPE%%|*}"
  SCOPE="${ROLE_SCOPE##*|}"
  az role assignment create \
    --assignee-object-id "$PRINCIPAL_ID" \
    --assignee-principal-type ServicePrincipal \
    --role "$ROLE" \
    --scope "$SCOPE"
done
```

RBAC changes can take a few minutes to propagate. A missing role shows up at
runtime as **HTTP 403**, and the API logs which service and role to check.

### App Configuration + Key Vault content

The identity also needs *data* to read:

```bash
# A plain value
az appconfig kv set --name "$APPCONFIG" --key "Demo:Message" \
  --value "Hello from Azure App Configuration" --yes

# A secret in Key Vault, exposed through App Configuration as a reference
az keyvault secret set --vault-name "$KEYVAULT" --name "DemoSecret" --value "super-secret-value"
SECRET_ID=$(az keyvault secret show --vault-name "$KEYVAULT" --name "DemoSecret" --query id -o tsv)
az appconfig kv set-keyvault --name "$APPCONFIG" --key "Demo:SecretValue" \
  --secret-identifier "$SECRET_ID" --yes
```

---

## Running locally

`DefaultAzureCredential` has no managed identity to use on your machine, so it
falls through the credential chain to your **Azure CLI** (`az login`) or
**Visual Studio** account. That means **your own user** needs the same roles as
the managed identity — assign them the same way, with your object id:

```bash
ME=$(az ad signed-in-user show --query id -o tsv)

for ROLE_SCOPE in \
  "App Configuration Data Reader|$APPCONFIG_ID" \
  "Key Vault Secrets User|$KEYVAULT_ID" \
  "Storage Blob Data Contributor|$STORAGE_ID" \
  "Storage Blob Data Delegator|$STORAGE_ID" \
  "EventGrid Data Sender|$TOPIC_ID"
do
  ROLE="${ROLE_SCOPE%%|*}"
  SCOPE="${ROLE_SCOPE##*|}"
  az role assignment create --assignee-object-id "$ME" --assignee-principal-type User \
    --role "$ROLE" --scope "$SCOPE"
done
```

Then:

```bash
az login
# put your real endpoints in IdentityLab.Api/appsettings.Development.json
#   (or: dotnet user-secrets — still only endpoint URLs, no keys)
dotnet run --project IdentityLab.Api
```

Open <https://localhost:7050/swagger> (see `IdentityLab.Api/Properties/launchSettings.json`
for ports).

### Minimal local check

`GET /health` needs nothing configured. `GET /api/config/demo` returns **503**
until `Azure:AppConfig:Endpoint` is set and the keys above exist. The file
endpoints need the Storage account name and Event Grid topic endpoint.

## Requirements

- .NET 8 SDK (the project multi‑targets nothing — just `net8.0`)
- An Azure subscription with the four resources above, if you want the Azure‑backed
  endpoints to actually work
