namespace Proxy.Options;

public class ProxyOptions
{
	public Uri? Target { get; set; }

	public string Username { get; set; } = null!;

	public string Password { get; set; } = null!;
}
