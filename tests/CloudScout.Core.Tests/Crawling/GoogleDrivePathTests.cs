using CloudScout.Core.Crawling.GoogleDrive;
using FluentAssertions;

namespace CloudScout.Core.Tests.Crawling;

/// <summary>
/// Google Drive has no native paths — the provider builds them by descending the folder tree.
/// These tests pin the path-building helper, since faking the full Google.Apis client to test
/// the full enumeration loop is heavy and yields little extra coverage.
/// </summary>
public class GoogleDrivePathTests
{
    [Fact]
    public void Root_child_path_starts_with_single_slash()
    {
        GoogleDriveProvider.BuildChildPath("/", "report.pdf").Should().Be("/report.pdf");
    }

    [Fact]
    public void Nested_child_path_appends_with_slash()
    {
        GoogleDriveProvider.BuildChildPath("/Documents", "report.pdf").Should().Be("/Documents/report.pdf");
    }

    [Fact]
    public void Deeply_nested_path_does_not_collapse_separators()
    {
        GoogleDriveProvider.BuildChildPath("/A/B/C", "file.txt").Should().Be("/A/B/C/file.txt");
    }
}
