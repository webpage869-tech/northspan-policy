using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Configuration;
using NorthspanPolicy;

namespace NorthspanPolicy.Tests;

public class TextExtractorTests
{
    private static MemoryStream MakeDocx(params string[] paragraphs)
    {
        var ms = new MemoryStream();
        using (var doc = WordprocessingDocument.Create(ms, WordprocessingDocumentType.Document, true))
        {
            var main = doc.AddMainDocumentPart();
            main.Document = new Document(new Body(paragraphs.Select(t => new Paragraph(new Run(new Text(t))))));
        }
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public async Task ReadsWordDocuments()
    {
        using var docx = MakeDocx("AI Policy", "Staff must not enter personal data.");
        var text = await TextExtractor.ExtractAsync(docx, "policy.docx");
        Assert.Contains("AI Policy", text);
        Assert.Contains("personal data", text);
    }

    [Fact]
    public async Task ReadsTextFiles()
    {
        using var ms = new MemoryStream("Plain policy text"u8.ToArray());
        Assert.Equal("Plain policy text", await TextExtractor.ExtractAsync(ms, "policy.txt"));
    }

    [Fact]
    public async Task RejectsOtherFileTypes()
    {
        using var ms = new MemoryStream([1, 2, 3]);
        await Assert.ThrowsAsync<UnsupportedFileException>(() => TextExtractor.ExtractAsync(ms, "policy.pdf"));
    }

    [Fact]
    public async Task RejectsBrokenWordFiles()
    {
        using var ms = new MemoryStream("not a zip"u8.ToArray());
        await Assert.ThrowsAsync<UnsupportedFileException>(() => TextExtractor.ExtractAsync(ms, "policy.docx"));
    }

    [Fact]
    public void TruncatesVeryLongText()
    {
        Assert.Equal(TextExtractor.MaxChars, TextExtractor.Clean(new string('a', TextExtractor.MaxChars + 500)).Length);
    }
}

public class AppStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"policy-test-{Guid.NewGuid():N}.db");
    private AppStore NewStore() => new(new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?> { ["Data:Path"] = _path }).Build());

    [Fact]
    public void Waitlist_IgnoresDuplicateEmailForSameProduct()
    {
        var store = NewStore();
        Assert.True(store.AddToWaitlist("Jane@Example.com", "Demo CU", "fix", true));
        Assert.False(store.AddToWaitlist("jane@example.com", null, "fix", false));
        Assert.True(store.AddToWaitlist("jane@example.com", null, "create", false));
        Assert.Equal(2, store.WaitlistCount());
    }

    [Fact]
    public void DailyCheckLimit_IsEnforced()
    {
        var store = NewStore();
        Assert.True(store.TryRecordCheck(2));
        Assert.True(store.TryRecordCheck(2));
        Assert.False(store.TryRecordCheck(2));
    }

    [Theory]
    [InlineData("jane@example.com", true)]
    [InlineData("not-an-email", false)]
    [InlineData("", false)]
    [InlineData("Jane Doe <jane@example.com>", false)]
    public void ValidatesEmail(string email, bool expected) => Assert.Equal(expected, AppStore.IsValidEmail(email));

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var f in new[] { _path, _path + "-wal", _path + "-shm" })
            if (File.Exists(f)) File.Delete(f);
    }
}
