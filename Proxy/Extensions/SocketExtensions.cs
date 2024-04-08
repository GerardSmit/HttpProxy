using System.Buffers;
using System.Net.Sockets;

namespace Proxy;

public static class SocketExtensions
{
	public static async ValueTask SendAsync(this Socket socket, ReadOnlySequence<byte> sequence)
	{
		foreach (var segment in sequence)
		{
			await socket.SendAsync(segment, SocketFlags.None);
		}
	}

	public static async ValueTask WriteAsync(this Stream stream, ReadOnlySequence<byte> sequence)
	{
		foreach (var segment in sequence)
		{
			await stream.WriteAsync(segment);
		}
	}
}