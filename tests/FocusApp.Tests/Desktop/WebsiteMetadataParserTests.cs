using FocusApp.Desktop.Services;
using Xunit;

namespace FocusApp.Tests.Desktop;

public sealed class WebsiteMetadataParserTests
{
    [Fact]
    public void DisplayNamePrefersOpenGraphThenTitleThenApplicationName()
    {
        const string allSources = """
            <html><head>
              <meta name="application-name" content="Application Name">
              <title>Page Title</title>
              <meta content="Open Graph Name" property="og:site_name">
            </head></html>
            """;
        const string titleAndApplication = """
            <html><head><meta name="application-name" content="Application Name"><title>Page Title</title></head></html>
            """;
        const string applicationOnly = """
            <html><head><meta content="Application Name" name="application-name"></head></html>
            """;

        Assert.Equal("Open Graph Name", WebsiteMetadataParser.ParseDisplayName(allSources));
        Assert.Equal("Page Title", WebsiteMetadataParser.ParseDisplayName(titleAndApplication));
        Assert.Equal("Application Name", WebsiteMetadataParser.ParseDisplayName(applicationOnly));
    }

    [Fact]
    public void DisplayNameDecodesEntitiesAndCollapsesWhitespace()
    {
        const string html = "<title>  Video &amp; Music\n  Site  </title>";

        Assert.Equal("Video & Music Site", WebsiteMetadataParser.ParseDisplayName(html));
        Assert.Null(WebsiteMetadataParser.ParseDisplayName("<html><head></head></html>"));
    }
}
