Build a demo ASP.NET Core 8 Web API in C# that shows how to use Azure managed 
identity to talk to several Azure services with no secrets or connection strings 
anywhere in the code or config.

## Services the API must use
1. Azure App Configuration — read application settings at startup
2. Azure Key Vault — read secrets, referenced through App Configuration
3. Azure Blob Storage — upload a file, and generate a time-limited download link
4. Azure Event Grid — publish an event after a successful upload

## Authentication requirement
Use DefaultAzureCredential from the Azure.Identity package for every service. 
Create it once and reuse the same instance. No account keys, no connection 
strings, no client secrets. The only values in configuration should be endpoint 
URLs and the storage account name.

## Endpoints
- POST /api/files/upload — accepts a multipart file, uploads it to a blob 
  container, publishes a "FileUploaded" event to Event Grid with the blob name 
  and size, returns the blob name.
- GET /api/files/{blobName}/download-url — returns a user delegation SAS URL 
  valid for 15 minutes. Use BlobServiceClient.GetUserDelegationKeyAsync and sign 
  a BlobSasBuilder with it — do NOT use an account key to sign.
- GET /api/config/demo — returns one value read from App Configuration and one 
  secret read from Key Vault, to prove both are wired up. Mask the secret in the 
  response (show only the first 3 characters).
- GET /health — simple liveness check.

## Structure
- Program.cs for DI registration and the credential setup
- A Services folder with IBlobStorageService / BlobStorageService and 
  IEventPublisher / EventGridPublisher, both interface-backed
- A Controllers folder with FilesController and ConfigController
- Register BlobServiceClient and EventGridPublisherClient as singletons
- Async all the way, cancellation tokens on the public methods
- Real error handling: catch RequestFailedException and return sensible status 
  codes, especially 403 when a role assignment is missing. Log the failing 
  service name so it's obvious which role is wrong.
- Swagger enabled in development

## Also produce
- appsettings.json with placeholder endpoint values and comments explaining each
- A README.md that lists, as a table, every Azure role assignment the managed 
  identity needs (service, role name, scope), plus the az cli commands to enable 
  the system-assigned identity and assign each role
- A short section in the README on running locally: explain that 
  DefaultAzureCredential falls back to the developer's az login / Visual Studio 
  account, and that the developer's own user needs the same roles assigned

Explain your choices as you go, and keep the code readable over clever — this is 
a learning project, not production.

github Url: https://github.com/mdyusufalam/IdentityLab.git
username: mdyusufalam@gmail.com
