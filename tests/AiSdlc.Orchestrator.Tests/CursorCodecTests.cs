using AiSdlc.Orchestrator.Events;
using Xunit;

namespace AiSdlc.Orchestrator.Tests;

public sealed class CursorCodecTests
{
    [Theory]
    [InlineData("00637847651311234567_abc123def456")]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("abc")]
    public void RoundTrip_PreservesOriginalValue(string original)
    {
        var encoded = CursorCodec.Encode(original);
        var ok = CursorCodec.TryDecode(encoded, out var decoded);

        // Empty string encodes to empty; TryDecode returns false on empty input.
        if (string.IsNullOrEmpty(original))
        {
            Assert.False(ok);
            return;
        }

        Assert.True(ok);
        Assert.Equal(original, decoded);
    }

    [Fact]
    public void Encoded_Output_UsesUrlSafeAlphabet()
    {
        // Pick a RowKey whose base64 will contain '+' and '/' so we can prove the URL-safe replacement runs.
        // Random bytes that hit those characters in standard base64:
        var rowKey = new string([(char)0xff, (char)0xfe, (char)0xfd, (char)0xfc]);
        var encoded = CursorCodec.Encode(rowKey);

        Assert.DoesNotContain('+', encoded);
        Assert.DoesNotContain('/', encoded);
        Assert.DoesNotContain('=', encoded);
    }

    [Fact]
    public void TryDecode_NullOrEmpty_ReturnsFalse()
    {
        Assert.False(CursorCodec.TryDecode(null, out _));
        Assert.False(CursorCodec.TryDecode(string.Empty, out _));
    }

    [Theory]
    [InlineData("!!!invalid!!!")]
    [InlineData("not base64 at all")]
    [InlineData("####")]
    public void TryDecode_Malformed_ReturnsFalse(string cursor)
    {
        Assert.False(CursorCodec.TryDecode(cursor, out _));
    }
}
