using Merp.Infrastructure.Documents;

namespace Merp.BuildingBlocks.UnitTests.Documents;

public sealed class RichTextSanitizerTests
{
    private readonly RichTextSanitizer _sanitizer = new();

    [Fact]
    public void Formatting_IsKept_AndLinksOpenSafely()
    {
        _sanitizer.Sanitize("<h2>Başlık</h2><p><strong>Kalın</strong>, <em>italik</em>, <u>altı</u>, <s>üstü</s><br>satır</p><ul><li>bir</li></ul><ol><li>iki</li></ol><blockquote><p>alıntı</p></blockquote>")
            .ShouldBe("<h2>Başlık</h2><p><strong>Kalın</strong>, <em>italik</em>, <u>altı</u>, <s>üstü</s><br>satır</p><ul><li>bir</li></ul><ol><li>iki</li></ol><blockquote><p>alıntı</p></blockquote>");
        _sanitizer.Sanitize("<p><a href=\"https://merp.example/kariyer\">site</a> <a href=\"mailto:ik@merp.example\">e-posta</a></p>")
            .ShouldBe("<p><a href=\"https://merp.example/kariyer\" target=\"_blank\" rel=\"noopener noreferrer nofollow\">site</a> <a href=\"mailto:ik@merp.example\" target=\"_blank\" rel=\"noopener noreferrer nofollow\">e-posta</a></p>");
    }

    [Fact]
    public void Scripts_EventHandlersStylesAndUnsafeLinks_AreRemoved()
    {
        var clean = _sanitizer.Sanitize("<p onclick=\"alert(1)\" style=\"color:red\" class=\"x\">metin<script>alert(1)</script></p><img src=x onerror=alert(1)><iframe src=\"https://evil\"></iframe><p><a href=\"javascript:alert(1)\">x</a></p>");

        clean.ShouldNotBeNull();
        clean.ShouldNotContain("script", Case.Insensitive);
        clean.ShouldNotContain("alert");
        clean.ShouldNotContain("onclick");
        clean.ShouldNotContain("style");
        clean.ShouldNotContain("img");
        clean.ShouldNotContain("iframe");
        clean.ShouldNotContain("javascript");
        clean.ShouldContain("<p>metin</p>");
    }

    [Fact]
    public void UnknownTags_KeepTheirText()
    {
        _sanitizer.Sanitize("<div><span>Kalan metin</span></div><h1>Büyük başlık</h1>").ShouldBe("Kalan metinBüyük başlık");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<p></p>")]
    [InlineData("<p><br></p><p> </p>")]
    [InlineData("<script>alert(1)</script>")]
    public void EmptyText_IsNull(string? html) => _sanitizer.Sanitize(html).ShouldBeNull();
}
