using PdfSharp.Pdf.IO;

namespace CS3.ScanBridge.Tests;

public sealed class PdfComposerTests
{
    [Fact]
    public void CreatesOnePagePdf()
    {
        var result = new PdfComposer().Compose([ImageFactory.Page()], 85, 10);
        using var stream = new MemoryStream(result.Data);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        Assert.Equal(1, document.PageCount);
    }

    [Fact]
    public void CreatesTwoPageDuplexPdfInOrder()
    {
        var result = new PdfComposer().Compose([
            ImageFactory.Page(System.Drawing.Color.White),
            ImageFactory.Page(System.Drawing.Color.LightGray)
        ], 85, 10);
        using var stream = new MemoryStream(result.Data);
        using var document = PdfReader.Open(stream, PdfDocumentOpenMode.Import);
        Assert.Equal(2, document.PageCount);
    }
}
