using System.Text;
using WinDeutsch.Core.Import;

namespace WinDeutsch.Core.Tests.Import;

public sealed class TextFileReaderTests : IDisposable
{
    private const string Sample = "Schöne Grüße\tС приветом, ёжик";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "WinDeutschTests", Guid.NewGuid().ToString("N"));

    public TextFileReaderTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private string Write(byte[] bytes)
    {
        var path = Path.Combine(_dir, Guid.NewGuid().ToString("N") + ".txt");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    [Fact]
    public void Reads_Utf8WithBom() =>
        Assert.Equal(Sample, TextFileReader.ReadAllTextDetect(Write(new UTF8Encoding(true).GetPreamble()
            .Concat(Encoding.UTF8.GetBytes(Sample)).ToArray())));

    [Fact]
    public void Reads_Utf8WithoutBom() =>
        Assert.Equal(Sample, TextFileReader.ReadAllTextDetect(Write(Encoding.UTF8.GetBytes(Sample))));

    [Fact]
    public void Reads_Windows1251()
    {
        // "С приветом" in cp1251, built by hand so the test does not depend on the code under test.
        var bytes = new byte[] { 0xD1, 0x20, 0xEF, 0xF0, 0xE8, 0xE2, 0xE5, 0xF2, 0xEE, 0xEC, 0x2C, 0x20, 0xB8, 0xE6, 0xE8, 0xEA };
        Assert.Equal("С приветом, ёжик", TextFileReader.ReadAllTextDetect(Write(bytes)));
    }
}
