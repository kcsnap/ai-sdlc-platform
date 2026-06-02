using System.Text;

namespace AiSdlc.Orchestrator.Events;

/// <summary>
/// Opaque base64-URL codec wrapping the storage RowKey. Per ADR-0004 the cursor is opaque to API clients —
/// only the server encodes and decodes; clients treat it as a string token.
/// </summary>
internal static class CursorCodec
{
    public static string Encode(string rowKey)
    {
        ArgumentNullException.ThrowIfNull(rowKey);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(rowKey))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static bool TryDecode(string? cursor, out string rowKey)
    {
        rowKey = string.Empty;
        if (string.IsNullOrEmpty(cursor))
        {
            return false;
        }

        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            var paddingLength = (4 - padded.Length % 4) % 4;
            if (paddingLength > 0)
            {
                padded += new string('=', paddingLength);
            }

            rowKey = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
