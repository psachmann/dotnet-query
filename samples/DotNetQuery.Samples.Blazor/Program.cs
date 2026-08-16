using DotNetQuery.Samples.Blazor.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();
builder.Services.AddDotNetQuery(options =>
    {
        options.ExecutionMode = QueryExecutionMode.Ssr;
        options.StaleTime       = TimeSpan.FromMinutes(1);   // data stays fresh for 1 minute
        options.CacheTime       = TimeSpan.FromMinutes(10);  // cache entries live 10 minutes after last subscriber
        options.RefetchInterval = TimeSpan.FromSeconds(30);  // automatically refetch every 30 seconds
    });
builder.Services.AddDotNetQuerySamplesShared();
builder.Services.AddRadzenComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
