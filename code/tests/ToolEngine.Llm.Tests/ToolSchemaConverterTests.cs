namespace ToolEngine.Llm.Tests;

using System.Text.Json;
using FluentAssertions;
using ToolEngine.Core.Domain.Enums;
using ToolEngine.Core.Domain.Schema;
using ToolEngine.Llm.Conversion;
using ToolEngine.Tools.Abstractions.Metadata;
using ToolEngine.Tools.Registry;
using Xunit;

public sealed class ToolSchemaConverterTests
{
    private static ToolDescriptor MakeDescriptor(
        string ns, string name, string description = "A test tool.",
        string whenToUse = "When you need to test.", string whenNotToUse = "When it is inappropriate.")
    {
        var schema   = ToolSchema.For<object>(description, whenToUse, whenNotToUse, [], new ToolParameter("input", "string", "The input."));
        var metadata = new ToolMetadata(ns, name, "v1", description, ToolType.Logic, schema, ToolSchema.Empty);
        return new ToolDescriptor(metadata, typeof(object));
    }

    [Fact]
    public void Convert_SanitizesDotsToDoubleUnderscore()
    {
        var converter = new ToolSchemaConverter();
        var result    = converter.Convert([MakeDescriptor("math", "calculate")]);

        result.Should().HaveCount(1);
        result[0].SanitizedName.Should().Be("math__calculate");
    }

    [Fact]
    public void Convert_PreservesOriginalFullName()
    {
        var converter = new ToolSchemaConverter();
        var result    = converter.Convert([MakeDescriptor("math", "calculate")]);

        result[0].OriginalFullName.Should().Be("math.calculate");
    }

    [Fact]
    public void Convert_Embeds_WhenToUse_InDescription()
    {
        var converter = new ToolSchemaConverter();
        var result    = converter.Convert([MakeDescriptor("math", "calculate", whenToUse: "When calculating numbers.")]);

        result[0].Description.Should().Contain("When to use: When calculating numbers.");
    }

    [Fact]
    public void Convert_EmbedS_WhenNotToUse_InDescription()
    {
        var converter = new ToolSchemaConverter();
        var result    = converter.Convert([MakeDescriptor("math", "calculate", whenNotToUse: "Not for strings.")]);

        result[0].Description.Should().Contain("When NOT to use: Not for strings.");
    }

    [Fact]
    public void Convert_InputSchemaJson_IsValidJsonSchema()
    {
        var converter = new ToolSchemaConverter();
        var result    = converter.Convert([MakeDescriptor("math", "calculate")]);

        var act = () => JsonDocument.Parse(result[0].InputSchemaJson);
        act.Should().NotThrow();
    }

    [Fact]
    public void Convert_SkipsDisabledTools()
    {
        var schema     = ToolSchema.For<object>("Disabled tool.");
        var metadata   = new ToolMetadata("math", "disabled", "v1", "Disabled tool.", ToolType.Logic, schema, ToolSchema.Empty, IsEnabled: false);
        var descriptor = new ToolDescriptor(metadata, typeof(object));

        var converter = new ToolSchemaConverter();
        var result    = converter.Convert([descriptor]);

        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData("math.calculate",        "math__calculate")]
    [InlineData("payment.charge-card",   "payment__charge-card")]
    [InlineData("weather.current",       "weather__current")]
    public void SanitizeName_ReplacesDots(string fullName, string expected)
        => ToolSchemaConverter.SanitizeName(fullName).Should().Be(expected);

    [Theory]
    [InlineData("math__calculate",       "math.calculate")]
    [InlineData("payment__charge-card",  "payment.charge-card")]
    public void DesanitizeName_RestoresDots(string sanitized, string expected)
        => ToolSchemaConverter.DesanitizeName(sanitized).Should().Be(expected);

    [Fact]
    public void SanitizeAndDesanitize_AreInverses()
    {
        const string fullName = "finance.transfer-funds";
        ToolSchemaConverter.DesanitizeName(
            ToolSchemaConverter.SanitizeName(fullName)).Should().Be(fullName);
    }
}
