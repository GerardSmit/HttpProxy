namespace Proxy;

public readonly record struct HttpRequestHeaderResult(Uri? Uri, HostString Host, HttpMethod Method);

public readonly record struct HttpResponseHeaderResult(int StatusCode);