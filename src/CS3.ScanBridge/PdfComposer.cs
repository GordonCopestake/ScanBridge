using System.Drawing;
using System.Drawing.Imaging;
using PdfSharp.Drawing;
using PdfSharp.Pdf;

namespace CS3.ScanBridge;

public sealed class PdfComposer : IPdfComposer
{
    public PdfComposition Compose(IReadOnlyList<ScanPage> pages, int jpegQuality, int maximumPages)
    {
        if (pages.Count == 0) throw new NoPagesException("No pages are available for PDF creation.");
        using var document = new PdfDocument();
        var pageCount = 0;
        foreach (var scanPage in pages)
        {
            using var source = new MemoryStream(scanPage.Data, false);
            using var image = Image.FromStream(source, true, true);
            var frameDimension = new FrameDimension(image.FrameDimensionsList[0]);
            var frameCount = image.GetFrameCount(frameDimension);
            for (var frame = 0; frame < frameCount; frame++)
            {
                if (pageCount >= maximumPages) break;
                image.SelectActiveFrame(frameDimension, frame);
                using var encoded = EncodeJpeg(image, jpegQuality);
                AddPage(document, encoded, image.Width, image.Height, SafeDpi(image.HorizontalResolution), SafeDpi(image.VerticalResolution));
                pageCount++;
            }
            if (pageCount >= maximumPages) break;
        }
        using var output = new MemoryStream();
        document.Save(output, false);
        return new(output.ToArray(), pageCount);
    }

    private static MemoryStream EncodeJpeg(Image image, int quality)
    {
        var output = new MemoryStream();
        var codec = ImageCodecInfo.GetImageEncoders().Single(value => value.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(Encoder.Quality, quality);
        image.Save(output, codec, parameters);
        output.Position = 0;
        return output;
    }

    private static void AddPage(PdfDocument document, Stream encoded, int width, int height, double dpiX, double dpiY)
    {
        using var xImage = XImage.FromStream(encoded);
        var page = document.AddPage();
        page.Width = XUnit.FromPoint(width / dpiX * 72d);
        page.Height = XUnit.FromPoint(height / dpiY * 72d);
        using var graphics = XGraphics.FromPdfPage(page);
        graphics.DrawImage(xImage, 0, 0, page.Width.Point, page.Height.Point);
    }

    private static double SafeDpi(float dpi) => dpi is > 20 and < 2400 ? dpi : 300d;
}
