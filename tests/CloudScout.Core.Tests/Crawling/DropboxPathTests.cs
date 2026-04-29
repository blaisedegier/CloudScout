using CloudScout.Core.Crawling.Dropbox;
using FluentAssertions;

namespace CloudScout.Core.Tests.Crawling;

/// <summary>
/// Dropbox returns full paths in metadata, so the provider's only computed path field is the
/// parent folder. These tests pin that helper — the rest of the provider just maps SDK fields
/// straight onto <see cref="Core.Crawling.RemoteFileMetadata"/> and is hard to stub usefully
/// without a full SDK fake.
/// </summary>
public class DropboxPathTests
{
    [Theory]
    [InlineData("/file.txt", "/")]
    [InlineData("/Documents/file.txt", "/Documents")]
    [InlineData("/A/B/C/file.txt", "/A/B/C")]
    public void Parent_path_strips_final_segment(string fullPath, string expected)
    {
        DropboxProvider.ParentPath(fullPath).Should().Be(expected);
    }

    [Fact]
    public void Empty_path_falls_back_to_root()
    {
        DropboxProvider.ParentPath(string.Empty).Should().Be("/");
    }
}
