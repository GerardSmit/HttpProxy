using System.Buffers;
using System.Net;
using System.Net.Security;
using System.Text;

namespace Proxy.Http;

public class HttpProxyHelper
{
	public static async ValueTask<SslStream> EstablishSocksTunnelAsync(Stream stream, string hostHost, int hostPort, Uri proxy, ICredentials? proxyCredentials)
	{
		var credentials = proxyCredentials?.GetCredential(proxy, "Basic");
		var sslStream = new SslStream(stream, leaveInnerStreamOpen: false);

		await sslStream.AuthenticateAsClientAsync(proxy.Host);

		await sslStream.WriteAsync(Encoding.ASCII.GetBytes($"CONNECT {hostHost}:{hostPort} HTTP/1.1\r\n"));

		if (credentials != null)
		{
			await sslStream.WriteAsync(Encoding.ASCII.GetBytes($"Proxy-Authorization: Basic {Convert.ToBase64String(Encoding.ASCII.GetBytes($"{credentials.UserName}:{credentials.Password}"))}\r\n"));
		}

		sslStream.Write("\r\n"u8);
		await sslStream.FlushAsync();

		var owner = ArrayPool<byte>.Shared.Rent(32768);
		Memory<byte> memory = owner;

		var read = await sslStream.ReadAsync(memory);
		var sequence = new ReadOnlySequence<byte>(memory.Slice(0, read));

		if (!HttpMessageParser.ParseResponse(sequence, out var parseResult, out var position))
		{
			throw new Exception();
		}

		ArrayPool<byte>.Shared.Return(owner);

		return sslStream;
	}
}
