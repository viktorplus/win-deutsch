using WinDeutsch.Core.Import;

namespace WinDeutsch.Core.Tests.Import;

public sealed class ImportParserTests
{
    [Theory]
    [InlineData("Guten Tag\tДобрый день")]
    [InlineData("Guten Tag;Добрый день")]
    [InlineData("Guten Tag — Добрый день")]
    [InlineData("Guten Tag - Добрый день")]
    public void Parse_DetectsSeparatorPerLine(string line)
    {
        var row = Assert.Single(ImportParser.Parse(line).Rows);
        Assert.Equal("Guten Tag", row.German);
        Assert.Equal("Добрый день", row.Russian);
        Assert.Null(row.TopicName);
    }

    [Fact]
    public void Parse_TabWinsOverOtherSeparators()
    {
        var row = Assert.Single(ImportParser.Parse("Na ja; gut - so\tНу; ладно - так").Rows);
        Assert.Equal("Na ja; gut - so", row.German);
        Assert.Equal("Ну; ладно - так", row.Russian);
    }

    [Fact]
    public void Parse_ThirdColumnIsTopic_AndLineNumbersCountSkippedLines()
    {
        var text = "# Kommentar\r\n\r\nDanke\tСпасибо\tВежливость\r\n   \r\nBitte;Пожалуйста;Вежливость\r\n";
        var result = ImportParser.Parse(text);

        Assert.Empty(result.Errors);
        Assert.Equal(2, result.Rows.Count);
        Assert.Equal(new ImportRow(3, "Danke", "Спасибо", "Вежливость"), result.Rows[0]);
        Assert.Equal(new ImportRow(5, "Bitte", "Пожалуйста", "Вежливость"), result.Rows[1]);
    }

    [Fact]
    public void Parse_LineWithoutSeparator_IsPhraseWithoutTranslation()
    {
        var row = Assert.Single(ImportParser.Parse("Alles klar").Rows);
        Assert.Equal("Alles klar", row.German);
        Assert.Equal("", row.Russian);
    }

    [Fact]
    public void Parse_EmptyGerman_IsErrorWithLineNumber()
    {
        var result = ImportParser.Parse("Ja\tДа\n\tНет\nNein\tНет");
        Assert.Equal(2, result.Rows.Count);
        var error = Assert.Single(result.Errors);
        Assert.Equal(2, error.LineNumber);
        Assert.Equal("пустой немецкий текст", error.Message);
    }

    [Fact]
    public void Normalize_TrimsCollapsesAndLowercases() =>
        Assert.Equal("guten morgen, frau müller", ImportParser.Normalize("  Guten \t Morgen,   Frau MÜLLER "));
}
