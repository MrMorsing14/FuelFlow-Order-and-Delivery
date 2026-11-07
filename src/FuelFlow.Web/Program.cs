using FuelFlow.Web;
using FuelFlow.Web.Api;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

// The API is on the same address as the UI (nginx forwards /api/ to it), so there is no base URL setting and no CORS.
builder.Services.AddFuelFlowWeb(new Uri(builder.HostEnvironment.BaseAddress));

await builder.Build().RunAsync();
