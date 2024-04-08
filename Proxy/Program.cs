using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Connections;
using Proxy;
using Proxy.Options;

var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
{
	Args = args
});

builder.Logging.AddConsole();
builder.Configuration.AddCommandLine(args);
builder.Configuration.AddEnvironmentVariables();
builder.Configuration.AddJsonFile("appsettings.json", optional: true);
builder.Configuration.AddJsonFile("appsettings.Development.json", optional: true);

builder.Services.AddOptions<ProxyOptions>()
	.Bind(builder.Configuration.GetSection("Proxy"));

var certificateOptions = builder.Configuration
	.GetSection("Certificate")
	.Get<CertificateOptions>();

builder.WebHost.UseKestrel((_, options) =>
{
	options.ListenAnyIP(89, listenOptions =>
	{
		listenOptions.UseConnectionLogging();

		if (certificateOptions is { IsConfigured: true })
		{
			listenOptions.UseHttps(adapterOptions =>
			{
				adapterOptions.ServerCertificate = new X509Certificate2(
					certificateOptions.Path,
					certificateOptions.Password,
					X509KeyStorageFlags.UserKeySet);
			});
		}

		listenOptions.UseConnectionHandler<ProxyHandler>();
	});
});

var app = builder.Build();

app.Run();