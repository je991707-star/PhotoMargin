using ImageMagick;
using ImageMagick.Drawing;
using PhotoMargin.Core;
using PhotoMargin.Core.Models;
using PhotoMargin.Imaging;

var repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
var inputDirectory = Path.Combine(repositoryRoot, "samples", "input");
var outputDirectory = Path.Combine(repositoryRoot, "samples", "output");
Directory.CreateDirectory(inputDirectory);
Directory.CreateDirectory(outputDirectory);

var cases = new (string Name, int Width, int Height, string Color)[]
{
    ("3x2", 1800, 1200, "#426A8C"),
    ("4x3", 1600, 1200, "#4E7C64"),
    ("16x9", 1920, 1080, "#805A76"),
    ("2x1", 2000, 1000, "#9A6746")
};

var loader = new ImageLoader(new ExifReader());
var measurer = new MagickTextMeasurer();
var engine = new LayoutEngine(measurer);
var renderer = new ImageRenderer();

foreach (var sample in cases)
{
    var inputPath = Path.Combine(inputDirectory, $"sample_{sample.Name}.png");
    using (var image = new MagickImage(new MagickColor(sample.Color), (uint)sample.Width, (uint)sample.Height))
    {
        new Drawables()
            .FillColor(new MagickColor("#33FFFFFF"))
            .Circle(sample.Width * 0.25, sample.Height * 0.35, sample.Width * 0.42, sample.Height * 0.35)
            .FillColor(new MagickColor("#22000000"))
            .Polygon([
                new PointD(0, sample.Height),
                new PointD(sample.Width * 0.42, sample.Height * 0.48),
                new PointD(sample.Width * 0.65, sample.Height * 0.72),
                new PointD(sample.Width, sample.Height * 0.35),
                new PointD(sample.Width, sample.Height)])
            .Draw(image);
        image.Write(inputPath);
    }

    using var photo = loader.Load(inputPath);
    var metadata = new DisplayMetadata(
        "Panasonic DC-GH5",
        "LEICA DG 12-60mm F2.8-4.0",
        "25mm",
        "f/5.6",
        "1/250s",
        "ISO 200",
        "2026.10.13");
    var settings = new LayoutSettings { OutputSize = 1080 };
    var layout = engine.Calculate(photo.OrientedSize, metadata, settings);
    if (!layout.IsValid)
    {
        throw new InvalidOperationException(string.Join(Environment.NewLine, layout.Errors));
    }

    File.WriteAllBytes(Path.Combine(outputDirectory, $"sample_{sample.Name}_1080.jpg"), renderer.RenderJpeg(photo, layout, 95));

    if (sample.Name == "3x2")
    {
        var largeSettings = settings with { OutputSize = 2048 };
        var largeLayout = engine.Calculate(photo.OrientedSize, metadata, largeSettings);
        File.WriteAllBytes(Path.Combine(outputDirectory, "sample_3x2_2048.jpg"), renderer.RenderJpeg(photo, largeLayout, 95));
    }
}

Console.WriteLine($"Generated {cases.Length + 1} outputs in {outputDirectory}");
