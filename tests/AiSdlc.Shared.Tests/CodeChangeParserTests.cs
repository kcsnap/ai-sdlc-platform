using AiSdlc.Shared;
using Xunit;

namespace AiSdlc.Shared.Tests;

public sealed class CodeChangeParserTests
{
    [Fact]
    public void Parse_SingleBlock_ReturnsOneFileChange()
    {
        var markdown = """
            ```path:README.md
            # Hello World
            This is the content.
            ```
            """;

        var result = CodeChangeParser.Parse(markdown);

        Assert.Single(result);
        Assert.Equal("README.md", result[0].Path);
        Assert.Contains("# Hello World", result[0].Content);
    }

    [Fact]
    public void Parse_MultipleBlocks_ReturnsAll()
    {
        var markdown = """
            ```path:README.md
            # Project
            ```
            ```path:src/api/Foo.cs
            public class Foo {}
            ```
            """;

        var result = CodeChangeParser.Parse(markdown);

        Assert.Equal(2, result.Count);
        Assert.Equal("README.md",    result[0].Path);
        Assert.Equal("src/api/Foo.cs", result[1].Path);
        Assert.Contains("# Project",       result[0].Content);
        Assert.Contains("public class Foo", result[1].Content);
    }

    [Fact]
    public void Parse_ProseOutsideBlocks_IsIgnored()
    {
        var markdown = """
            Here is the implementation:

            ```path:README.md
            # Hello
            ```

            Done!
            """;

        var result = CodeChangeParser.Parse(markdown);

        Assert.Single(result);
        Assert.Equal("README.md", result[0].Path);
    }

    [Fact]
    public void Parse_NoBlocks_ReturnsEmpty()
    {
        var result = CodeChangeParser.Parse("This has no code blocks at all.");
        Assert.Empty(result);
    }

    [Fact]
    public void Parse_NullInput_ReturnsEmpty()
    {
        Assert.Empty(CodeChangeParser.Parse(null));
    }

    [Fact]
    public void Parse_EmptyInput_ReturnsEmpty()
    {
        Assert.Empty(CodeChangeParser.Parse(string.Empty));
    }

    [Fact]
    public void Parse_PathWithDirectory_PreservesFullPath()
    {
        var markdown = """
            ```path:src/frontend/src/components/SearchBar.tsx
            export default function SearchBar() {}
            ```
            """;

        var result = CodeChangeParser.Parse(markdown);

        Assert.Single(result);
        Assert.Equal("src/frontend/src/components/SearchBar.tsx", result[0].Path);
    }
}
