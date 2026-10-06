using System.Text;

namespace WinDeutsch.Core.Import;

public static class TextFileReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    static TextFileReader() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    // UTF-8 with BOM, valid UTF-8 without BOM, otherwise Windows-1251.
    public static string ReadAllTextDetect(string path)
    {
        var bytes = File.ReadAllBytes(path);
        var bom = Encoding.UTF8.Preamble;
        if (bytes.AsSpan().StartsWith(bom))
            return StrictUtf8.GetString(bytes, bom.Length, bytes.Length - bom.Length);
        try
        {
            return StrictUtf8.GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            return Encoding.GetEncoding(1251).GetString(bytes);
        }
    }
}
