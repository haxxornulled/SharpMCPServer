using System.Text.Json;
using MCPServer.Infrastructure.Mcp.JsonSchema;
using MCPServer.Domain.Mcp;
using Xunit;

namespace MCPServer.UnitTests.Mcp;

public sealed class McpToolArgumentValidatorTests
{
    [Fact]
    public void Validate_Allows_Empty_Arguments_For_No_Parameter_Object_Schema()
    {
        var validator = new JsonSchemaNetToolArgumentValidator();
        using var schemaDocument = JsonDocument.Parse("""
        {
          "type": "object",
          "additionalProperties": false
        }
        """);

        var result = TestFin.Success(validator.Validate(schemaDocument.RootElement, arguments: default(JsonElement?)));

        Assert.Equal(JsonValueKind.Object, result.ValueKind);
    }

    [Fact]
    public void Validate_Rejects_Additional_Properties_When_Disabled()
    {
        var validator = new JsonSchemaNetToolArgumentValidator();
        using var schemaDocument = JsonDocument.Parse("""
        {
          "type": "object",
          "additionalProperties": false
        }
        """);
        using var argumentDocument = JsonDocument.Parse("""{"unexpected":true}""");

        var error = TestFin.Failure(validator.Validate(schemaDocument.RootElement, argumentDocument.RootElement));

        Assert.Contains("additional propert", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_Enforces_Required_Property_And_Primitive_Type()
    {
        var validator = new JsonSchemaNetToolArgumentValidator();
        using var schemaDocument = JsonDocument.Parse("""
        {
          "type": "object",
          "required": ["name"],
          "properties": {
            "name": { "type": "string", "minLength": 1 }
          },
          "additionalProperties": false
        }
        """);
        using var argumentDocument = JsonDocument.Parse("""{"name":42}""");

        var error = TestFin.Failure(validator.Validate(schemaDocument.RootElement, argumentDocument.RootElement));

        Assert.Contains("string", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_Enforces_Array_Item_Schema()
    {
        var validator = new JsonSchemaNetToolArgumentValidator();
        using var schemaDocument = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "ids": {
              "type": "array",
              "items": { "type": "integer" }
            }
          },
          "additionalProperties": false
        }
        """);
        using var argumentDocument = JsonDocument.Parse("""{"ids":[1,"bad"]}""");

        var error = TestFin.Failure(validator.Validate(schemaDocument.RootElement, argumentDocument.RootElement));

        Assert.Contains("$.ids[1]", error.Message, StringComparison.Ordinal);
        Assert.Contains("integer", error.Message, StringComparison.OrdinalIgnoreCase);
    }
    [Fact]
    public void Validate_Rejects_Unsupported_Schema_Dialect()
    {
        var validator = new JsonSchemaNetToolArgumentValidator();
        using var schemaDocument = JsonDocument.Parse("""
        {
          "$schema": "http://json-schema.org/draft-07/schema#",
          "type": "object"
        }
        """);

        var error = TestFin.Failure(validator.Validate(schemaDocument.RootElement, arguments: default(JsonElement?)));

        Assert.Contains("not supported", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_Accepts_Default_2020_12_Dialect()
    {
        var validator = new JsonSchemaNetToolArgumentValidator();
        using var schemaDocument = JsonDocument.Parse("""
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "additionalProperties": false
        }
        """);

        var result = TestFin.Success(validator.Validate(schemaDocument.RootElement, arguments: default(JsonElement?)));

        Assert.Equal(JsonValueKind.Object, result.ValueKind);
    }

    [Fact]
    public void ValidateRequiredValue_Rejects_Missing_Structured_Output()
    {
        var validator = new JsonSchemaNetToolArgumentValidator();
        using var schemaDocument = JsonDocument.Parse("""
        {
          "type": "object",
          "required": ["name"],
          "properties": { "name": { "type": "string" } },
          "additionalProperties": false
        }
        """);
        using var payloadDocument = JsonDocument.Parse("""{}""");

        var error = TestFin.Failure(validator.ValidateRequiredValue(schemaDocument.RootElement, payloadDocument.RootElement, "Tool structuredContent"));

        Assert.Contains("Tool structuredContent", error.Message, StringComparison.Ordinal);
        Assert.Contains("required property", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_Enforces_OneOf_Composition()
    {
        var validator = new JsonSchemaNetToolArgumentValidator();
        using var schemaDocument = JsonDocument.Parse("""
        {
          "type": "object",
          "properties": {
            "value": {
              "oneOf": [
                { "type": "string", "const": "alpha" },
                { "type": "integer", "minimum": 10 }
              ]
            }
          },
          "required": ["value"],
          "additionalProperties": false
        }
        """);
        using var argumentDocument = JsonDocument.Parse("""{"value":5}""");

        var error = TestFin.Failure(validator.Validate(schemaDocument.RootElement, argumentDocument.RootElement));

        Assert.Contains("oneOf", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Validate_Enforces_PatternProperties_And_UnevaluatedProperties()
    {
        var validator = new JsonSchemaNetToolArgumentValidator();
        using var schemaDocument = JsonDocument.Parse("""
        {
          "type": "object",
          "patternProperties": {
            "^x-[a-z]+$": { "type": "integer" }
          },
          "unevaluatedProperties": false
        }
        """);
        using var argumentDocument = JsonDocument.Parse("""{"x-count":"bad","other":1}""");

        var error = TestFin.Failure(validator.Validate(schemaDocument.RootElement, argumentDocument.RootElement));

        Assert.Contains("JSON Schema", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_Enforces_Ref_And_Defs()
    {
        var validator = new JsonSchemaNetToolArgumentValidator();
        using var schemaDocument = JsonDocument.Parse("""
        {
          "type": "object",
          "$defs": {
            "positiveId": { "type": "integer", "minimum": 1 }
          },
          "properties": {
            "id": { "$ref": "#/$defs/positiveId" }
          },
          "required": ["id"],
          "additionalProperties": false
        }
        """);
        using var argumentDocument = JsonDocument.Parse("""{"id":0}""");

        var error = TestFin.Failure(validator.Validate(schemaDocument.RootElement, argumentDocument.RootElement));

        Assert.Contains("minimum", error.Message, StringComparison.OrdinalIgnoreCase);
    }

}
