using System.Diagnostics.CodeAnalysis;

namespace Proxy.Options;

public class CertificateOptions
{
	public string? Path { get; set; }

	public string? Password { get; set; }

	[MemberNotNullWhen(true, nameof(Path), nameof(Password))]
	public bool IsConfigured => !string.IsNullOrWhiteSpace(Path) && !string.IsNullOrWhiteSpace(Password);
}
