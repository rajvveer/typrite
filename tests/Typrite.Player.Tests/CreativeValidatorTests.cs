using System.Security.Cryptography;
using Typrite.Contracts.Delivery;
using Typrite.Player.Creatives;

namespace Typrite.Player.Tests;

public class CreativeValidatorTests
{
    private static readonly string Hash = Convert.ToHexStringLower(SHA256.HashData([1, 2, 3]));

    private static CreativeManifest Image(string format = "400x225", string mediaType = "image/png", long size = 1024,
        string? click = null, string content = "https://cdn.example.com/c/1.png") =>
        new(Guid.NewGuid(), format, mediaType, size, Hash, DurationMs: null, new Uri(content), click);

    private static CreativeManifest Video(int? durationMs = 15000, long size = 5_000_000) =>
        new(Guid.NewGuid(), "400x225", "video/mp4", size, Hash, durationMs, new Uri("https://cdn.example.com/v/1.mp4"), null);

    [Fact]
    public void Valid_image_manifest_passes()
    {
        Assert.Empty(CreativeValidator.Validate(Image(), CreativePolicy.Default));
    }

    [Fact]
    public void Valid_video_manifest_passes()
    {
        Assert.Empty(CreativeValidator.Validate(Video(), CreativePolicy.Default));
    }

    [Theory]
    [InlineData("400x225")]
    [InlineData("384x216")]
    [InlineData("300x250")]
    public void All_three_contract_formats_are_accepted(string format)
    {
        Assert.Empty(CreativeValidator.Validate(Image(format), CreativePolicy.Default));
    }

    [Fact]
    public void Unknown_format_is_rejected()
    {
        Assert.Contains(CreativeValidator.Validate(Image("728x90"), CreativePolicy.Default), v => v.Contains("format", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData("text/html")]
    [InlineData("application/javascript")]
    [InlineData("video/webm")]
    [InlineData("image/svg+xml")]
    public void Disallowed_media_types_are_rejected(string mediaType)
    {
        Assert.NotEmpty(CreativeValidator.Validate(Image(mediaType: mediaType), CreativePolicy.Default));
    }

    [Fact]
    public void Oversized_image_is_rejected()
    {
        Assert.NotEmpty(CreativeValidator.Validate(Image(size: CreativePolicy.Default.MaxImageBytes + 1), CreativePolicy.Default));
    }

    [Fact]
    public void Video_without_duration_or_too_long_is_rejected()
    {
        Assert.NotEmpty(CreativeValidator.Validate(Video(durationMs: null), CreativePolicy.Default));
        Assert.NotEmpty(CreativeValidator.Validate(Video(durationMs: 31_000), CreativePolicy.Default));
    }

    [Fact]
    public void Non_https_content_or_clickthrough_is_rejected()
    {
        Assert.NotEmpty(CreativeValidator.Validate(Image(content: "http://cdn.example.com/c.png"), CreativePolicy.Default));
        Assert.NotEmpty(CreativeValidator.Validate(Image(click: "javascript:alert(1)"), CreativePolicy.Default));
        Assert.NotEmpty(CreativeValidator.Validate(Image(click: "file:///C:/x"), CreativePolicy.Default));
        Assert.Empty(CreativeValidator.Validate(Image(click: "https://advertiser.example.com/"), CreativePolicy.Default));
    }

    [Fact]
    public void Malformed_hash_is_rejected()
    {
        var m = Image() with { Sha256 = "abc" };

        Assert.NotEmpty(CreativeValidator.Validate(m, CreativePolicy.Default));
    }

    [Fact]
    public void Bytes_must_match_declared_hash_size_and_magic()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
        var m = Image(size: png.Length) with { Sha256 = Convert.ToHexStringLower(SHA256.HashData(png)) };

        Assert.Empty(CreativeValidator.VerifyBytes(m, png));
        Assert.NotEmpty(CreativeValidator.VerifyBytes(m, [.. png, 0]));                       // size and hash differ
        Assert.NotEmpty(CreativeValidator.VerifyBytes(m with { MediaType = "image/jpeg" }, png)); // magic mismatch
    }

    [Fact]
    public void Mp4_magic_is_checked_at_the_ftyp_box()
    {
        byte[] mp4 = [0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m', 0, 0, 0, 0];
        var m = Video(size: mp4.Length) with { Sha256 = Convert.ToHexStringLower(SHA256.HashData(mp4)) };

        Assert.Empty(CreativeValidator.VerifyBytes(m, mp4));
    }
}
