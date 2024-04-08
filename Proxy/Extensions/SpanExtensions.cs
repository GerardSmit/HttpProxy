using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Proxy;

internal static class SpanExtensions
{
	public static bool SequenceEqualIgnoreCaseOrdinal(this ReadOnlySpan<byte> xSpan, ReadOnlySpan<byte> ySpan)
	{
		if (xSpan.Length != ySpan.Length)
		{
			return false;
		}

		if (xSpan.SequenceEqual(ySpan))
		{
			return true;
		}

		if (xSpan.Length <= 32)
		{
			return Equals32(xSpan, ySpan);
		}

		return EqualsSlow(xSpan, ySpan);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static void CopyLowerCased(ReadOnlySpan<byte> source, Span<byte> destination)
	{
		source.CopyTo(destination);

		while (true)
		{
			var index = destination.IndexOfAnyInRange((byte)'A', (byte)'Z');

			if (index == -1)
			{
				return;
			}

			Unsafe.Add(ref MemoryMarshal.GetReference(destination), index) += 32;
			destination = destination.Slice(index + 1);
		}
	}

	private static bool Equals32(ReadOnlySpan<byte> xSpan, ReadOnlySpan<byte> ySpan)
	{
		Span<byte> xSpan32 = stackalloc byte[32];
		Span<byte> ySpan32 = stackalloc byte[32];

		CopyLowerCased(xSpan, xSpan32);
		CopyLowerCased(ySpan, ySpan32);

		return xSpan32.SequenceEqual(ySpan32);
	}

	private static bool EqualsSlow(ReadOnlySpan<byte> xSpan, ReadOnlySpan<byte> ySpan)
	{
		ref var xRef = ref MemoryMarshal.GetReference(xSpan);
		ref var yRef = ref MemoryMarshal.GetReference(ySpan);
		var length = xSpan.Length;

		for (var i = 0; i < length; i++)
		{
			var xChar = Unsafe.Add(ref xRef, i);
			var yChar = Unsafe.Add(ref yRef, i);

			if (xChar == yChar)
			{
				continue;
			}

			if (xChar is >= 65 and <= 90)
			{
				xChar += 32;
			}

			if (yChar is >= 65 and <= 90)
			{
				yChar += 32;
			}

			if (xChar != yChar)
			{
				return false;
			}
		}

		return true;
	}
}
