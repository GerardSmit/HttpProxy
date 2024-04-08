using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Proxy.Http;

public static class HttpMessageParser
{
	public static bool ParseResponse(ReadOnlySequence<byte> buffer, out HttpResponseResult result, out SequencePosition position)
	{
		var reader = new SequenceReader<byte>(buffer);

		if (!ParseResponseHeader(ref reader, out var http))
		{
			position = default;
			result = default;
			return false;
		}

		if (!ParseHeaders(ref reader, out var headers))
		{
			position = default;
			result = default;
			return false;
		}

		position = reader.Position;
		result = new HttpResponseResult(http.StatusCode, headers);
		return true;
	}

	public static bool ParseRequest(ReadOnlySequence<byte> buffer, out HttpRequestResult result, out SequencePosition position, out bool http2GoAway)
	{
		var reader = new SequenceReader<byte>(buffer);

		if (reader.IsNext("PRI * HTTP/2.0\r\n\r\nSM\r\n\r\n"u8, advancePast: true))
		{
			http2GoAway = true;
			position = buffer.End;
			result = default;
			return true;
		}

		http2GoAway = false;

		if (!ParseRequestHeader(ref reader, out var http))
		{
			position = default;
			result = default;
			return false;
		}

		if (!ParseHeaders(ref reader, out var headers))
		{
			position = default;
			result = default;
			return false;
		}

		position = reader.Position;
		result = new HttpRequestResult(http.Uri, http.Host, http.Method, headers);
		return true;
	}

	private static bool ParseRequestHeader(ref SequenceReader<byte> reader, out HttpRequestHeaderResult result)
	{
		if (!reader.TryReadTo(out ReadOnlySequence<byte> line, "\r\n"u8))
		{
			result = default;
			return false;
		}

		var lineReader = new SequenceReader<byte>(line);

		if (!lineReader.TryReadTo(out ReadOnlySpan<byte> method, " "u8))
		{
			result = default;
			return false;
		}

		if (!lineReader.TryReadTo(out ReadOnlySpan<byte> path, " "u8))
		{
			result = default;
			return false;
		}

		HttpMethod? methodEnum = null;

		switch (method[0])
		{
			case (byte)'G':
				if (method.SequenceEqual("GET"u8)) methodEnum = HttpMethod.Get;
				break;
			case (byte)'C':
				if (method.SequenceEqual("CONNECT"u8)) methodEnum = HttpMethod.Connect;
				break;
			case (byte)'P':
				if (method.SequenceEqual("POST"u8)) methodEnum = HttpMethod.Post;
				if (method.SequenceEqual("PUT"u8)) methodEnum = HttpMethod.Put;
				break;
			case (byte)'D':
				if (method.SequenceEqual("DELETE"u8)) methodEnum = HttpMethod.Delete;
				break;
			case (byte)'H':
				if (method.SequenceEqual("HEAD"u8)) methodEnum = HttpMethod.Head;
				break;
			case (byte)'O':
				if (method.SequenceEqual("OPTIONS"u8)) methodEnum = HttpMethod.Options;
				break;
			case (byte)'T':
				if (method.SequenceEqual("TRACE"u8)) methodEnum = HttpMethod.Trace;
				break;
		}

		if (methodEnum == null)
		{
			Span<char> chars = stackalloc char[20];
			var length = Encoding.ASCII.GetChars(method, chars);

			methodEnum = HttpMethod.Parse(chars.Slice(0, length));
		}

		var hostStr = Encoding.UTF8.GetString(path);

		HostString host;
		Uri? uri;

		if (hostStr.StartsWith("http:", StringComparison.OrdinalIgnoreCase) || hostStr.StartsWith("https:", StringComparison.OrdinalIgnoreCase))
		{
			uri = new Uri(hostStr);
			host = new HostString(uri.Host, uri.Port);
		}
		else
		{
			uri = null;
			host = new HostString(hostStr);
		}

		result = new HttpRequestHeaderResult(uri, host, methodEnum);
		return true;
	}

	private static bool ParseResponseHeader(ref SequenceReader<byte> reader, out HttpResponseHeaderResult result)
	{
		// Example: HTTP/1.1 200 OK

		if (!reader.TryReadTo(out ReadOnlySequence<byte> line, "\r\n"u8))
		{
			result = default;
			return false;
		}

		var lineReader = new SequenceReader<byte>(line);

		if (!lineReader.TryReadTo(out ReadOnlySpan<byte> version, " "u8))
		{
			result = default;
			return false;
		}

		if (!lineReader.TryReadTo(out ReadOnlySpan<byte> statusCodeBytes, " "u8) || statusCodeBytes.Length > 3)
		{
			result = default;
			return false;
		}

		Span<char> statusCodeChars = stackalloc char[3];
		var statusCodeLength = Encoding.ASCII.GetChars(statusCodeBytes, statusCodeChars);

		if (!int.TryParse(statusCodeChars.Slice(0, statusCodeLength), out var statusCode))
		{
			result = default;
			return false;
		}

		result = new HttpResponseHeaderResult(statusCode);
		return true;
	}

	private static bool ParseHeaders(ref SequenceReader<byte> reader, [NotNullWhen(true)] out Dictionary<HeaderName, ReadOnlyMemory<byte>>? headers)
	{
		headers = new Dictionary<HeaderName, ReadOnlyMemory<byte>>();

		while (true)
		{
			if (!reader.TryReadTo(out ReadOnlySequence<byte> line, "\r\n"u8))
			{
				return false;
			}

			if (line.Length == 0)
			{
				return true;
			}

			var span = line.IsSingleSegment
				? line.First
				: line.ToArray(); // TODO: avoid allocation

			var index = span.Span.IndexOf((byte)':');

			if (index == -1)
			{
				return false;
			}

			var left = span.Slice(0, index).TrimEnd(" "u8);
			var right = span.Slice(index + 1).TrimStart(" "u8);

			headers.Add(new HeaderName(left), right);
		}
	}
}

[DebuggerDisplay("{DebuggerDisplay,nq}")]
public readonly struct HeaderName : IEquatable<HeaderName>
{
	public static readonly HeaderName ProxyAuthorization = new("Proxy-Authorization"u8.ToArray());

	private readonly ReadOnlyMemory<byte> _memory;
	private readonly int _hashCode;

	public HeaderName(ReadOnlyMemory<byte> memory)
	{
		_memory = memory;
		_hashCode = CalculateHashCode(memory);
	}

	internal string DebuggerDisplay => Encoding.ASCII.GetString(_memory.Span);

	public ReadOnlySpan<byte> Span => _memory.Span;

	public bool Equals(HeaderName other)
	{
		return _memory.Span.SequenceEqualIgnoreCaseOrdinal(other._memory.Span);
	}

	public bool Equals(ReadOnlySpan<byte> other)
	{
		return _memory.Span.SequenceEqualIgnoreCaseOrdinal(other);
	}

	public override bool Equals(object? obj)
	{
		return obj is HeaderName other && Equals(other);
	}

	public override int GetHashCode()
	{
		return _hashCode;
	}

	public static bool operator ==(HeaderName left, HeaderName right)
	{
		return left.Equals(right);
	}

	public static bool operator !=(HeaderName left, HeaderName right)
	{
		return !left.Equals(right);
	}

	private static int CalculateHashCode(ReadOnlyMemory<byte> memory)
	{
		var hashCode = new HashCode();
		Span<byte> span = stackalloc byte[memory.Length];
		SpanExtensions.CopyLowerCased(memory.Span, span);
		hashCode.AddBytes(span);
		return hashCode.ToHashCode();
	}
}

