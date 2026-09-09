using Azure.Identity;
using Azure.Messaging.EventGrid;
using Azure.Storage.Blobs;
using IdentityLab.Api.Infrastructure;
using IdentityLab.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------------------
// 1. ONE credential, created once, reused for every Azure client.
//
// DefaultAzureCredential walks an ordered chain of credential sources and uses
// the first one that produces a token:
//   environment variables -> workload identity -> managed identity ->
//   Visual Studio -> Azure CLI (`az login`) -> Azure PowerShell -> ...
//
// Running in Azure, it lands on the app's managed identity.
// Running on a dev box, it falls through to your `az login` / Visual Studio
// sign-in. The application code is identical in both places, and there is no
// secret to store or rotate.
//
// The SDK caches tokens internally, so sharing a single instance is both the
// documented guidance and cheaper than newing one up per client.
// ---------------------------------------------------------------------------
var credential = new DefaultAzureCredential();

// ---------------------------------------------------------------------------
// 2. Azure App Configuration is added as an extra IConfiguration source, so the
//    rest of the app just reads IConfiguration and never knows the difference.
//
//    Key Vault references stored in App Configuration are resolved during
//    startup by ConfigureKeyVault, again using the same managed identity. That
//    is how "read a secret from Key Vault" happens without a SecretClient
//    anywhere in our code.
//
//    optional: true means the app still boots if the endpoint is unreachable
//    (handy for `/health` and for running pieces of the demo in isolation).
//    ConfigController reports a clear 503 if the expected keys are missing.
// ---------------------------------------------------------------------------
var appConfigEndpoint = builder.Configuration["Azure:AppConfig:Endpoint"];
if (!string.IsNullOrWhiteSpace(appConfigEndpoint) && Uri.IsWellFormedUriString(appConfigEndpoint, UriKind.Absolute))
{
    builder.Configuration.AddAzureAppConfiguration(options =>
    {
        options.Connect(new Uri(appConfigEndpoint), credential)
               .ConfigureKeyVault(kv => kv.SetCredential(credential));
    }, optional: true);
}
else
{
    Console.WriteLine("[startup] Azure:AppConfig:Endpoint is not set to a valid URL - " +
                      "skipping App Configuration. /api/config/demo will return 503.");
}

// ---------------------------------------------------------------------------
// 3. Azure SDK clients as singletons.
//
//    The Azure SDK clients are thread-safe and expensive to build, so the
//    guidance is one instance for the life of the process. Each is constructed
//    from an endpoint (or account name) plus the shared credential - no keys,
//    no connection strings.
// ---------------------------------------------------------------------------
builder.Services.AddSingleton(credential);

builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var accountName = config["Azure:Storage:AccountName"];
    if (string.IsNullOrWhiteSpace(accountName))
    {
        throw new ConfigurationMissingException("Azure:Storage:AccountName");
    }

    // Build the well-known blob endpoint from the account name so config only
    // ever holds the name, never a full connection string.
    var blobEndpoint = new Uri($"https://{accountName}.blob.core.windows.net");
    return new BlobServiceClient(blobEndpoint, credential);
});

builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var topicEndpoint = config["Azure:EventGrid:TopicEndpoint"];
    if (string.IsNullOrWhiteSpace(topicEndpoint))
    {
        throw new ConfigurationMissingException("Azure:EventGrid:TopicEndpoint");
    }

    return new EventGridPublisherClient(new Uri(topicEndpoint), credential);
});

builder.Services.AddScoped<IBlobStorageService, BlobStorageService>();
builder.Services.AddScoped<IEventPublisher, EventGridPublisher>();

// ---------------------------------------------------------------------------
// 4. Standard Web API plumbing.
// ---------------------------------------------------------------------------
builder.Services.AddControllers(options => options.Filters.Add<ProblemExceptionFilter>());
builder.Services.AddHealthChecks();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();
