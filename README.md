# HttpProxy
Simple HTTP 1.1 proxy in C# with support for HTTPS and basic authentication.

Note: when a HTTP 2.0 request is received, the proxy will send a "Go away" response so HTTP 1.1 will be enforced (tested in Chrome).