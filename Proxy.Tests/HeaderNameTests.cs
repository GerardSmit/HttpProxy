using Proxy.Http;

namespace Proxy.Tests;

public class HeaderNameTests
{
	[Fact]
	public void EqualsTest()
	{
		var headerName = new HeaderName("Proxy-Authorization"u8.ToArray());

		Assert.True(headerName.Equals("Proxy-Authorization"u8));
	}

	[Fact]
	public void NotEqualsTest()
	{
		var headerName = new HeaderName("Proxy-Authorization"u8.ToArray());

		Assert.False(headerName.Equals("Pruxy-Authorization"u8));
	}

	[Fact]
	public void IgnoreCaseTest()
	{
		var headerName = new HeaderName("Proxy-Authorization"u8.ToArray());

		Assert.True(headerName.Equals("proxy-authorization"u8));
	}

	[Fact]
	public void DictionaryTest()
	{
		var headerName = new HeaderName("Proxy-Authorization"u8.ToArray());
		var headerNameLowerCased = new HeaderName("proxy-authorization"u8.ToArray());
		var dictionary = new Dictionary<HeaderName, int>
		{
			[headerName] = 1
		};

		Assert.True(dictionary.ContainsKey(headerName));
		Assert.True(dictionary.ContainsKey(headerNameLowerCased));
	}
}
