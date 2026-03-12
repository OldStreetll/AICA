using System;
using System.Collections.Generic;
using AICA.Core.Agent;
using Xunit;

namespace AICA.Core.Tests.Agent
{
    public class ToolParameterValidatorTests
    {
        [Fact]
        public void GetRequiredParameter_WithValidString_ReturnsValue()
        {
            // Arrange
            var args = new Dictionary<string, object> { ["name"] = "test" };

            // Act
            var result = ToolParameterValidator.GetRequiredParameter<string>(args, "name");

            // Assert
            Assert.Equal("test", result);
        }

        [Fact]
        public void GetRequiredParameter_WithMissingParameter_ThrowsException()
        {
            // Arrange
            var args = new Dictionary<string, object>();

            // Act & Assert
            Assert.Throws<ToolParameterException>(() =>
                ToolParameterValidator.GetRequiredParameter<string>(args, "name"));
        }

        [Fact]
        public void GetRequiredParameter_WithNullValue_ThrowsException()
        {
            // Arrange
            var args = new Dictionary<string, object> { ["name"] = null };

            // Act & Assert
            Assert.Throws<ToolParameterException>(() =>
                ToolParameterValidator.GetRequiredParameter<string>(args, "name"));
        }

        [Fact]
        public void GetRequiredParameter_WithIntConversion_ReturnsConvertedValue()
        {
            // Arrange
            var args = new Dictionary<string, object> { ["count"] = "42" };

            // Act
            var result = ToolParameterValidator.GetRequiredParameter<int>(args, "count");

            // Assert
            Assert.Equal(42, result);
        }

        [Fact]
        public void GetRequiredParameter_WithCustomConverter_UsesConverter()
        {
            // Arrange
            var args = new Dictionary<string, object> { ["value"] = "hello" };

            // Act
            var result = ToolParameterValidator.GetRequiredParameter<int>(args, "value",
                obj => obj.ToString().Length);

            // Assert
            Assert.Equal(5, result);
        }

        [Fact]
        public void GetOptionalParameter_WithMissingParameter_ReturnsDefault()
        {
            // Arrange
            var args = new Dictionary<string, object>();

            // Act
            var result = ToolParameterValidator.GetOptionalParameter<string>(args, "name", "default");

            // Assert
            Assert.Equal("default", result);
        }

        [Fact]
        public void GetOptionalParameter_WithNullValue_ReturnsDefault()
        {
            // Arrange
            var args = new Dictionary<string, object> { ["name"] = null };

            // Act
            var result = ToolParameterValidator.GetOptionalParameter<string>(args, "name", "default");

            // Assert
            Assert.Equal("default", result);
        }

        [Fact]
        public void GetOptionalParameter_WithValidValue_ReturnsValue()
        {
            // Arrange
            var args = new Dictionary<string, object> { ["name"] = "test" };

            // Act
            var result = ToolParameterValidator.GetOptionalParameter<string>(args, "name", "default");

            // Assert
            Assert.Equal("test", result);
        }

        [Fact]
        public void GetOptionalParameter_WithInvalidConversion_ReturnsDefault()
        {
            // Arrange
            var args = new Dictionary<string, object> { ["count"] = "not-a-number" };

            // Act
            var result = ToolParameterValidator.GetOptionalParameter<int>(args, "count", 10);

            // Assert
            Assert.Equal(10, result);
        }

        [Fact]
        public void ValidateRange_WithValueInRange_DoesNotThrow()
        {
            // Act & Assert
            ToolParameterValidator.ValidateRange(5, 1, 10, "value");
        }

        [Fact]
        public void ValidateRange_WithValueBelowMin_ThrowsException()
        {
            // Act & Assert
            Assert.Throws<ToolParameterException>(() =>
                ToolParameterValidator.ValidateRange(0, 1, 10, "value"));
        }

        [Fact]
        public void ValidateRange_WithValueAboveMax_ThrowsException()
        {
            // Act & Assert
            Assert.Throws<ToolParameterException>(() =>
                ToolParameterValidator.ValidateRange(11, 1, 10, "value"));
        }

        [Fact]
        public void ValidateNotEmpty_WithEmptyString_ThrowsException()
        {
            // Act & Assert
            Assert.Throws<ToolParameterException>(() =>
                ToolParameterValidator.ValidateNotEmpty("", "param"));
        }

        [Fact]
        public void ValidateNotEmpty_WithWhitespaceString_ThrowsException()
        {
            // Act & Assert
            Assert.Throws<ToolParameterException>(() =>
                ToolParameterValidator.ValidateNotEmpty("   ", "param"));
        }

        [Fact]
        public void ValidateNotEmpty_WithValidString_DoesNotThrow()
        {
            // Act & Assert
            ToolParameterValidator.ValidateNotEmpty("test", "param");
        }

        [Fact]
        public void ValidateNotEmpty_WithEmptyCollection_ThrowsException()
        {
            // Arrange
            var collection = new List<string>();

            // Act & Assert
            Assert.Throws<ToolParameterException>(() =>
                ToolParameterValidator.ValidateNotEmpty(collection, "param"));
        }

        [Fact]
        public void ValidateNotEmpty_WithNonEmptyCollection_DoesNotThrow()
        {
            // Arrange
            var collection = new List<string> { "item" };

            // Act & Assert
            ToolParameterValidator.ValidateNotEmpty(collection, "param");
        }

        [Fact]
        public void ValidateEnum_WithValidValue_DoesNotThrow()
        {
            // Arrange
            var allowed = new[] { "read", "write", "delete" };

            // Act & Assert
            ToolParameterValidator.ValidateEnum("read", allowed, "operation");
        }

        [Fact]
        public void ValidateEnum_WithInvalidValue_ThrowsException()
        {
            // Arrange
            var allowed = new[] { "read", "write", "delete" };

            // Act & Assert
            Assert.Throws<ToolParameterException>(() =>
                ToolParameterValidator.ValidateEnum("execute", allowed, "operation"));
        }

        [Fact]
        public void ValidatePattern_WithMatchingPattern_DoesNotThrow()
        {
            // Act & Assert
            ToolParameterValidator.ValidatePattern("test123", @"^[a-z]+\d+$", "value");
        }

        [Fact]
        public void ValidatePattern_WithNonMatchingPattern_ThrowsException()
        {
            // Act & Assert
            Assert.Throws<ToolParameterException>(() =>
                ToolParameterValidator.ValidatePattern("test", @"^\d+$", "value"));
        }

        [Fact]
        public void ValidateStringLength_WithValidLength_DoesNotThrow()
        {
            // Act & Assert
            ToolParameterValidator.ValidateStringLength("test", 1, 10, "value");
        }

        [Fact]
        public void ValidateStringLength_WithTooShort_ThrowsException()
        {
            // Act & Assert
            Assert.Throws<ToolParameterException>(() =>
                ToolParameterValidator.ValidateStringLength("a", 2, 10, "value"));
        }

        [Fact]
        public void ValidateStringLength_WithTooLong_ThrowsException()
        {
            // Act & Assert
            Assert.Throws<ToolParameterException>(() =>
                ToolParameterValidator.ValidateStringLength("toolong", 1, 5, "value"));
        }
    }
}
