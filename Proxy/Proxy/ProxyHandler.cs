using System.Buffers;
using System.Buffers.Text;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Options;
using Proxy.Http;
using Proxy.Options;
using Proxy.Socks;

namespace Proxy;

public class ProxyHandler(ILogger<ProxyHandler> logger, IOptions<ProxyOptions> options) : ConnectionHandler
{
	private readonly byte[] _username = Encoding.ASCII.GetBytes(options.Value.Username);
	private readonly byte[] _password = Encoding.ASCII.GetBytes(options.Value.Password);

	public async ValueTask<Stream> Connect(HostString host)
	{
		var proxy = options.Value.Target;

		var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);

		IPAddress ip;
		int port;

		if (proxy is not null)
		{
			ip = (await Dns.GetHostAddressesAsync(proxy.Host, AddressFamily.InterNetwork))[0];
			port = proxy.Port;
		}
		else
		{
			ip = (await Dns.GetHostAddressesAsync(host.Host, AddressFamily.InterNetwork))[0];
			port = host.Port ?? 443;
		}

		logger.LogInformation("CONNECT {Host}:{Port}", ip, port);

		await socket.ConnectAsync(ip, port);

		Stream stream = new NetworkStream(socket, ownsSocket: true);

		if (proxy is not null)
		{
			ICredentials? credentials = null;

			if (!string.IsNullOrEmpty(proxy.UserInfo))
			{
				var index = proxy.UserInfo.IndexOf(':');
				var username = proxy.UserInfo.Substring(0, index);
				var password = proxy.UserInfo.Substring(index + 1);

				credentials = new NetworkCredential(username, password);
			}

			if (proxy.Scheme is "socks5" or "socks4" or "socks4a")
			{
				await SocksHelper.EstablishSocksTunnelAsync(stream, host.Host, host.Port ?? 443, proxy, credentials, true, default);
			}
			else if (proxy.Scheme is "https")
			{
				stream = await HttpProxyHelper.EstablishSocksTunnelAsync(stream, host.Host, host.Port ?? 443, proxy, credentials);
			}
		}

		return stream;
	}

	public override async Task OnConnectedAsync(ConnectionContext connection)
	{
		var input = connection.Transport.Input;
		var output = connection.Transport.Output;

		HttpRequestResult parseResult;
		ReadOnlySequence<byte> remaining;

		while (true)
		{
			var result = await input.ReadAsync();

			if (!HttpMessageParser.ParseRequest(result.Buffer, out parseResult, out var position, out var http2GoAway))
			{
				return;
			}

			if (http2GoAway)
			{
				output.Write((ReadOnlySpan<byte>)[0, 0, 8, 7, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 13]);
				await output.FlushAsync();
				return;
			}

			remaining = result.Buffer.Slice(position);
			input.AdvanceTo(position);

			if (!parseResult.Headers.TryGetValue(HeaderName.ProxyAuthorization, out var proxyConnection))
			{
				output.Write("HTTP/1.1 407 Proxy Authentication Required\r\nProxy-Authenticate: Basic realm=\"Proxy\"\r\n\r\n"u8);
				await output.FlushAsync();
				continue;
			}

			if (!ValidateAuth(proxyConnection.Span))
			{
				output.Write("HTTP/1.1 401 Unauthorized\r\nProxy-Authenticate: Basic realm=\"Proxy\"\r\n\r\n"u8);
				await output.FlushAsync();
				continue;
			}

			break;

		}

		await using var stream = await Connect(parseResult.Host);

		if (parseResult.Method == HttpMethod.Connect)
		{
			output.Write("HTTP/1.1 200 OK\r\n\r\n"u8);
			await output.FlushAsync();

			if (remaining.Length > 0)
			{
				await stream.WriteAsync(remaining);
				await stream.FlushAsync();
			}
		}
		else
		{
			stream.Write(Encoding.ASCII.GetBytes(parseResult.Method.ToString()));
			stream.Write(" "u8);
			stream.Write(Encoding.ASCII.GetBytes(parseResult.Uri?.PathAndQuery ?? "/"));
			stream.Write(" HTTP/1.1\r\n"u8);
			await stream.FlushAsync();

			foreach (var header in parseResult.Headers)
			{
				stream.Write(header.Key.Span);
				stream.Write(": "u8);
				await stream.WriteAsync(header.Value);
				stream.Write("\r\n"u8);
				await stream.FlushAsync();
			}

			stream.Write("\r\n"u8);
			await stream.FlushAsync();
		}

		Task<int>? readFromSocket = null;
		Task<ReadResult>? readFromPipe = null;

		var buffer = new byte[8192];

		while (true)
		{
			readFromSocket ??= stream.ReadAsync(buffer).AsTask();
			readFromPipe ??= connection.Transport.Input.ReadAsync().AsTask();

			await Task.WhenAny(readFromSocket, readFromPipe);

			if (readFromSocket.IsCompleted)
			{
				var bytesRead = await readFromSocket;

				if (bytesRead == 0)
				{
					break;
				}

				await output.WriteAsync(buffer.AsMemory(0, bytesRead));
				await output.FlushAsync();
				readFromSocket = null;
			}

			if (readFromPipe.IsCompleted)
			{
				var readResult = await readFromPipe;

				if (readResult.IsCompleted)
				{
					break;
				}

				await stream.WriteAsync(readResult.Buffer);
				connection.Transport.Input.AdvanceTo(readResult.Buffer.End);
				readFromPipe = null;
			}
		}

		await output.CompleteAsync();
		await connection.Transport.Input.CompleteAsync();
	}

	private bool ValidateAuth(ReadOnlySpan<byte> base64Span)
	{
		if (!base64Span.Slice(0, 6).SequenceEqualIgnoreCaseOrdinal("Basic "u8))
		{
			return false;
		}

		base64Span = base64Span.Slice(6);

		var bytes = ArrayPool<byte>.Shared.Rent(1024);

		if (Base64.DecodeFromUtf8(base64Span, bytes.AsSpan(), out _, out var bytesWritten) != OperationStatus.Done)
		{
			return false;
		}

		var span = bytes.AsSpan(0, bytesWritten);
		var index = span.IndexOf((byte)':');
		var username = span.Slice(0, index);
		var password = span.Slice(index + 1);

		return username.SequenceEqual(_username) && password.SequenceEqual(_password);
	}
}