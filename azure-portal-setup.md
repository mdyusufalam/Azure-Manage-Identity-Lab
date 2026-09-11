# Azure portal setup

A portal-only walkthrough for standing up the four Azure services this demo talks
to. Everything authenticates with `DefaultAzureCredential`, so the whole job is:
create four resources, give an identity the right RBAC roles on each, add a little
content, and copy four endpoint values into config. **No keys, connection
strings, or client secrets anywhere** — that is the entire point of the project.

For the `az` CLI equivalents of the role assignments, see the loops in
[README.md](README.md).

---

## 0. Prerequisites

- A resource group (**Resource groups → Create**). Put everything below in it.
- Decide who the "identity" is:
  - **Running in Azure** → the App Service's system-assigned managed identity (Step 6).
  - **Running locally** → your own Entra ID user; `DefaultAzureCredential` falls
    through to your `az login` / Visual Studio sign-in. You do the same role
    assignments, just against your user instead of the managed identity.
  - Running both places → do the assignments in Step 7 for **both** principals.

---

## 1. Azure App Configuration

1. **Create a resource → "App Configuration" → Create.**
   - Resource group, a name, a region. Free or Standard tier is fine.
2. Open the resource → **Overview** → copy the **Endpoint**
   (`https://<name>.azconfig.io`). This is `Azure:AppConfig:Endpoint`.
3. Add the plain demo value: **Operations → Configuration explorer → Create →
   Key-value**
   - Key: `Demo:Message`
   - Value: `Hello from Azure App Configuration`
   - Apply. (`Demo:SecretValue` is added as a Key Vault reference in Step 3.)

---

## 2. Azure Key Vault

1. **Create a resource → "Key Vault" → Create.**
   - Resource group, name, region.
   - On the **Access configuration** tab, choose **Azure role-based access
     control** (not access policies).
2. Add the demo secret: open the vault → **Objects → Secrets → Generate/Import**
   - Name: `DemoSecret`
   - Secret value: `super-secret-value`
   - Create.
3. Open the secret → its current version → copy the **Secret Identifier**
   (`https://<vault>.vault.azure.net/secrets/DemoSecret/<version>`).

---

## 3. Link the Key Vault secret into App Configuration

Back in **App Configuration → Configuration explorer → Create → Key Vault
reference**:

- Key: `Demo:SecretValue`
- Subscription / Key Vault: pick your vault
- Secret: `DemoSecret`
- Apply.

App Configuration now exposes both `Demo:Message` and `Demo:SecretValue`; the app
resolves the reference at startup with the shared credential (no `SecretClient` in
code).

---

## 4. Azure Storage account

1. **Create a resource → "Storage account" → Create.**
   - Resource group, name — this is `Azure:Storage:AccountName` (just the name, no
     URL; the blob endpoint is derived in `Program.cs`).
   - Standard / LRS is fine.
2. Optional: pre-create the container. **Data storage → Containers → + Container**,
   name `uploads` (must match `Azure:Storage:ContainerName`). It is created on
   first upload if missing.
3. No keys needed. Optionally **Settings → Configuration → disable "Allow storage
   account key access"** to prove the point.

---

## 5. Azure Event Grid topic

Use a **custom topic**, not a system topic.

1. **Create a resource → "Event Grid Topic" → Create.**
   - Resource group, name, region.
2. Open it → **Overview** → copy the **Topic Endpoint**
   (`https://<name>.<region>-1.eventgrid.azure.net/api/events`). This is
   `Azure:EventGrid:TopicEndpoint`.
3. Optional, to watch events land: **Event Subscriptions → + Event Subscription**,
   endpoint type Web Hook / Storage Queue / Event Hub.

---

## 6. Enable the App Service managed identity

Skip if you are only running locally.

1. Create/open the **App Service** that will host the API.
2. **Settings → Identity → System assigned → Status = On → Save.**
3. Note the **Object (principal) ID** it produces — that is the assignee for the
   role assignments below.

---

## 7. Assign RBAC roles

For **each** resource: open it → **Access control (IAM) → + Add → Add role
assignment** → pick the role → **Members**: the App Service managed identity
(Step 6) and/or your own user for local dev → **Review + assign**.

| Resource | Role | Why |
| --- | --- | --- |
| App Configuration | **App Configuration Data Reader** | read key-values at startup |
| Key Vault | **Key Vault Secrets User** | resolve the Key Vault reference from App Config |
| Storage account | **Storage Blob Data Contributor** | create container + upload blobs |
| Storage account | **Storage Blob Delegator** | get the user delegation key to sign the 15-min SAS — a **separate role**, not included in Data Contributor |
| Event Grid Topic | **EventGrid Data Sender** | publish `FileUploaded` events |

- That is **5 assignments per identity**. Running both in Azure and locally means
  doing all 5 twice (managed identity + your user).
- RBAC propagation takes a few minutes. A missing role surfaces at runtime as
  **HTTP 403**, and the API log names the service and the exact role to check.
- If your Key Vault is on **access policies** instead of RBAC: skip the Key Vault
  role and instead **Key Vault → Access policies → Create → Secret permissions:
  Get, List → principal = the identity**.

---

## 8. Put the four endpoint values into config

Only endpoint URLs + the storage account name — never keys.

**Local dev** — edit `IdentityLab.Api/appsettings.Development.json` (or use
`dotnet user-secrets`):

| Setting | Value from |
| --- | --- |
| `Azure:AppConfig:Endpoint` | Step 1.2 |
| `Azure:Storage:AccountName` | Step 4.1 (name only) |
| `Azure:Storage:ContainerName` | `uploads` (or your container) |
| `Azure:EventGrid:TopicEndpoint` | Step 5.2 |

**In Azure** — App Service → **Settings → Environment variables → App settings**,
same keys with `__` (double underscore) instead of `:`:

- `Azure__AppConfig__Endpoint`
- `Azure__Storage__AccountName`
- `Azure__Storage__ContainerName`
- `Azure__EventGrid__TopicEndpoint`

---

## 9. Verify

| Check | Expectation |
| --- | --- |
| `GET /health` | 200 with nothing configured |
| `GET /api/config/demo` | 200 returning `Demo:Message` and a masked `Demo:SecretValue`; 503 if the App Config endpoint is unset or the keys are missing |
| `POST /api/files/upload` (multipart `file`) | 200 with the blob name; blob appears in `uploads`; `FileUploaded` on the topic |
| `GET /api/files/{blobName}/download-url` | 200 with a `...&sig=...` SAS URL that downloads for 15 minutes |

- **403** on any call → the matching role from Step 7 has not propagated or was
  not assigned to the identity actually in use.
- **503** → a required endpoint value from Step 8 is missing.
