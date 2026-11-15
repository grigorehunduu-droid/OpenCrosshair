namespace OpenCrosshair.Tests;

public class CommunityTests
{
    [Fact]
    public void ShareLinkFillsInTheFormWithAWorkingCode()
    {
        var style = new CrosshairStyle { Name = "My & Co cross", Gap = 7, ImagePath = @"C:\Users\Someone\pic.png", Games = ["cs2.exe"] };
        var url = new Uri(Community.ShareUrl(style));
        Assert.Equal("github.com", url.Host);
        Assert.Equal("/grigorehunduu-droid/OpenCrosshair/issues/new", url.AbsolutePath);

        var query = url.Query.TrimStart('?').Split('&')
            .Select(p => p.Split('=', 2))
            .ToDictionary(p => p[0], p => Uri.UnescapeDataString(p[1]));
        Assert.Equal("crosshair.yml", query["template"]);
        Assert.Equal("My & Co cross", query["name"]);            // the & doesn't break the link
        Assert.True(CrosshairStyle.TryParseShareCode(query["code"], out var shared));
        Assert.Equal(7, shared.Gap);
        Assert.Null(shared.ImagePath);                            // nothing private goes into the form
        Assert.Null(shared.Games);
        Assert.DoesNotContain("Someone", url.ToString());
    }
}
