using TSolve.Demo.Options;
using TSolve.Demo.Services;
using TSolve.Demo.Services.Jira;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.Configure<JiraOptions>(builder.Configuration.GetSection(JiraOptions.Section));
builder.Services.AddSingleton<DemoStore>();
builder.Services.AddSingleton<TextProcessingService>();
builder.Services.AddSingleton<ExcelTicketReader>();
builder.Services.AddSingleton<MockJiraClient>();
builder.Services.AddHttpClient<RealJiraClient>(client => client.Timeout = TimeSpan.FromSeconds(45));
builder.Services.AddSingleton<JiraClientSelector>();
builder.Services.AddSingleton<KnowledgePipelineService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}
app.UseRouting();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();


app.Run();
