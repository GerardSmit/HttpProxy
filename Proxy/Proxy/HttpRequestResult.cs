using Proxy.Http;

namespace Proxy;

public readonly record struct HttpRequestResult(Uri? Uri, HostString Host, HttpMethod Method, Dictionary<HeaderName, ReadOnlyMemory<byte>> Headers);

public readonly record struct HttpResponseResult(int StatusCode, Dictionary<HeaderName, ReadOnlyMemory<byte>> Headers);