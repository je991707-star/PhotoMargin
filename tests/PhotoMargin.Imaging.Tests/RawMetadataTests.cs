using System.Security.Cryptography;
using System.Text.Json;

namespace PhotoMargin.Imaging.Tests;

public sealed class RawMetadataTests
{
    [Fact]
    public async Task Rw2MetadataFixtureDoesNotInventCaptureTimeFromFileOrModifyDate()
    {
        var token = TestContext.Current.CancellationToken;
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".rw2");
        try
        {
            // Minimal TIFF/RW2 metadata container, NOT a real camera RAW sample.
            // Maker/model and EXIF ModifyDate only, no capture date or sensor data.
            using (var writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write(new byte[] { 0x49, 0x49, 0x55, 0x00 }); writer.Write(8u); writer.Write((ushort)3);
                writer.Write((ushort)0x010f); writer.Write((ushort)2); writer.Write(10u); writer.Write(50u);
                writer.Write((ushort)0x0110); writer.Write((ushort)2); writer.Write(7u); writer.Write(60u);
                writer.Write((ushort)0x0132); writer.Write((ushort)2); writer.Write(20u); writer.Write(67u);
                writer.Write(0u); writer.Write(System.Text.Encoding.ASCII.GetBytes("Panasonic\0DC-GH5\02025:01:02 03:04:05\0"));
            }
            var value = await new RawMetadataReader().ReadAsync(path, token);
            Assert.Equal("DC-GH5", value.Model); Assert.Null(value.CapturedAt); Assert.Null(value.DateSource); Assert.Null(value.LensModel);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task RawExtensionAndContentAreValidatedAndCancellationIsRespected()
    {
        var token = TestContext.Current.CancellationToken;
        var reader = new RawMetadataReader();
        await Assert.ThrowsAsync<NotSupportedException>(() => reader.ReadAsync("not-supported.nef", token));
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".rw2");
        try
        {
            await File.WriteAllBytesAsync(path, new byte[] { 0xff, 0xd8, 0xff, 0xd9, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }, token);
            await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReadAsync(path, token));
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadAsync(path, cancellation.Token));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task ConfiguredGh5Rw2IsReadWithoutDecodingOrWriting()
    {
        var path = Environment.GetEnvironmentVariable("PHOTOMARGIN_RW2_SAMPLE");
        if (string.IsNullOrWhiteSpace(path)) { Assert.Skip("Set PHOTOMARGIN_RW2_SAMPLE to a real GH5 RW2 for an optional read-only integration check."); return; }
        var token = TestContext.Current.CancellationToken;
        var before = await Hash(path, token);
        var metadata = await new RawMetadataReader().ReadAsync(path, token);
        Assert.Contains("GH5", metadata.Model ?? "", StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(metadata.CapturedAt); Assert.False(string.IsNullOrWhiteSpace(metadata.LensModel));
        Assert.True(metadata.FocalLengthMm > 0); Assert.True(metadata.FNumber > 0);
        Assert.True(metadata.ExposureSeconds > 0); Assert.True(metadata.Iso > 0);
        Assert.Equal(before, await Hash(path, token));
        var proof = Environment.GetEnvironmentVariable("PHOTOMARGIN_RAW_PROOF");
        if (!string.IsNullOrWhiteSpace(proof))
            await File.WriteAllTextAsync(proof, JsonSerializer.Serialize(new { Metadata = metadata, Sha256Unchanged = true,
                Reader = "MetadataExtractor 2.9.3", MetadataOnly = true }, new JsonSerializerOptions { WriteIndented = true }), token);
    }
    private static async Task<string> Hash(string path, CancellationToken token)
    { await using var stream = File.OpenRead(path); return Convert.ToHexString(await SHA256.HashDataAsync(stream, token)); }
}
