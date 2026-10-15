using PhotoMargin.Core.Models;
using PhotoMargin.Imaging;

namespace PhotoMargin.App.Services;

public sealed class PreviewService(ImageRenderer renderer)
{
    public Task<byte[]> RenderAsync(
        LoadedPhoto photo,
        LayoutResult layout,
        bool showGuides,
        CancellationToken cancellationToken) =>
        renderer.RenderPreviewAsync(photo, layout, showGuides, cancellationToken);
}
