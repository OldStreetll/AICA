using System;
using System.Collections.Generic;

namespace AICA.Core.Agent
{
    /// <summary>
    /// Exception thrown when tool parameter validation fails
    /// </summary>
    public class ToolParameterException : Exception
    {
        public ToolParameterException(string message) : base(message) { }
        public ToolParameterException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// Utility class for validating and extracting tool parameters
    /// Reduces code duplication across tools by providing common validation patterns
    /// </summary>
    public static class ToolParameterValidator
    {
        /// <summary>
        /// Get a required parameter and convert it to the specified type
        /// </summary>
        /// <typeparam name="T">The target type</typeparam>
        /// <param name="arguments">The arguments dictionary from ToolCall</param>
        /// <param name="paramName">The parameter name</param>
        /// <param name="converter">Optional custom converter function</param>
        /// <returns>The converted parameter value</returns>
        /// <exception cref="ToolParameterException">If parameter is missing or conversion fails</exception>
        public static T GetRequiredParameter<T>(
            Dictionary<string, object> arguments,
            string paramName,
            Func<object, T> converter = null)
        {
            if (arguments == null)
                throw new ArgumentNullException(nameof(arguments));

            if (string.IsNullOrWhiteSpace(paramName))
                throw new ArgumentException("Parameter name cannot be empty", nameof(paramName));

            if (!arguments.TryGetValue(paramName, out var value) || value == null)
                throw new ToolParameterException($"Missing required parameter: {paramName}");

            try
            {
                if (converter != null)
                    return converter(value);

                // Handle string type specially
                if (typeof(T) == typeof(string))
                    return (T)(object)value.ToString();

                // Try direct cast first
                if (value is T typedValue)
                    return typedValue;

                // Handle Nullable<T>: unwrap to underlying type before conversion
                var targetType = typeof(T);
                var underlyingType = Nullable.GetUnderlyingType(targetType);
                if (underlyingType != null)
                    return (T)Convert.ChangeType(value, underlyingType);

                // Try Convert.ChangeType for non-nullable types
                return (T)Convert.ChangeType(value, targetType);
            }
            catch (Exception ex) when (!(ex is ToolParameterException))
            {
                throw new ToolParameterException(
                    $"Invalid parameter '{paramName}': cannot convert '{value}' to {typeof(T).Name}. {ex.Message}",
                    ex);
            }
        }

        /// <summary>
        /// Get an optional parameter with a default value
        /// </summary>
        /// <typeparam name="T">The target type</typeparam>
        /// <param name="arguments">The arguments dictionary from ToolCall</param>
        /// <param name="paramName">The parameter name</param>
        /// <param name="defaultValue">The default value if parameter is missing</param>
        /// <param name="converter">Optional custom converter function</param>
        /// <returns>The converted parameter value or default</returns>
        public static T GetOptionalParameter<T>(
            Dictionary<string, object> arguments,
            string paramName,
            T defaultValue = default,
            Func<object, T> converter = null)
        {
            if (arguments == null)
                throw new ArgumentNullException(nameof(arguments));

            if (string.IsNullOrWhiteSpace(paramName))
                throw new ArgumentException("Parameter name cannot be empty", nameof(paramName));

            if (!arguments.TryGetValue(paramName, out var value) || value == null)
                return defaultValue;

            try
            {
                if (converter != null)
                    return converter(value);

                // Handle string type specially
                if (typeof(T) == typeof(string))
                    return (T)(object)value.ToString();

                // Try direct cast first
                if (value is T typedValue)
                    return typedValue;

                // Handle Nullable<T>: unwrap to underlying type before conversion
                var targetType = typeof(T);
                var underlyingType = Nullable.GetUnderlyingType(targetType);
                if (underlyingType != null)
                    return (T)Convert.ChangeType(value, underlyingType);

                // Try Convert.ChangeType for non-nullable types
                return (T)Convert.ChangeType(value, targetType);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[AICA] Optional parameter '{paramName}' conversion failed: " +
                    $"cannot convert '{value}' ({value?.GetType().Name}) to {typeof(T).Name}. " +
                    $"Using default: {defaultValue}. Error: {ex.Message}");
                return defaultValue;
            }
        }

        /// <summary>
        /// Validate that a value is within a specified range
        /// </summary>
        /// <typeparam name="T">The value type (must implement IComparable)</typeparam>
        /// <param name="value">The value to validate</param>
        /// <param name="min">The minimum allowed value (inclusive)</param>
        /// <param name="max">The maximum allowed value (inclusive)</param>
        /// <param name="paramName">The parameter name for error messages</param>
        /// <exception cref="ToolParameterException">If value is outside the range</exception>
        public static void ValidateRange<T>(T value, T min, T max, string paramName)
            where T : IComparable<T>
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            if (value.CompareTo(min) < 0 || value.CompareTo(max) > 0)
                throw new ToolParameterException(
                    $"Parameter '{paramName}' value {value} is out of range [{min}, {max}]");
        }

        /// <summary>
        /// Validate that a string is not empty
        /// </summary>
        /// <param name="value">The string to validate</param>
        /// <param name="paramName">The parameter name for error messages</param>
        /// <exception cref="ToolParameterException">If string is null or empty</exception>
        public static void ValidateNotEmpty(string value, string paramName)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ToolParameterException($"Parameter '{paramName}' cannot be empty");
        }

        /// <summary>
        /// Validate that a collection is not empty
        /// </summary>
        /// <typeparam name="T">The collection element type</typeparam>
        /// <param name="collection">The collection to validate</param>
        /// <param name="paramName">The parameter name for error messages</param>
        /// <exception cref="ToolParameterException">If collection is null or empty</exception>
        public static void ValidateNotEmpty<T>(ICollection<T> collection, string paramName)
        {
            if (collection == null || collection.Count == 0)
                throw new ToolParameterException($"Parameter '{paramName}' cannot be empty");
        }

        /// <summary>
        /// Validate that a value matches one of the allowed options
        /// </summary>
        /// <typeparam name="T">The value type</typeparam>
        /// <param name="value">The value to validate</param>
        /// <param name="allowedValues">The allowed values</param>
        /// <param name="paramName">The parameter name for error messages</param>
        /// <exception cref="ToolParameterException">If value is not in allowed values</exception>
        public static void ValidateEnum<T>(T value, IEnumerable<T> allowedValues, string paramName)
            where T : IEquatable<T>
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));

            if (allowedValues == null)
                throw new ArgumentNullException(nameof(allowedValues));

            foreach (var allowed in allowedValues)
            {
                if (value.Equals(allowed))
                    return;
            }

            throw new ToolParameterException(
                $"Parameter '{paramName}' value '{value}' is not one of the allowed values");
        }

        /// <summary>
        /// Validate that a string matches a pattern
        /// </summary>
        /// <param name="value">The string to validate</param>
        /// <param name="pattern">The regex pattern</param>
        /// <param name="paramName">The parameter name for error messages</param>
        /// <exception cref="ToolParameterException">If string doesn't match pattern</exception>
        public static void ValidatePattern(string value, string pattern, string paramName)
        {
            if (string.IsNullOrEmpty(value))
                throw new ToolParameterException($"Parameter '{paramName}' cannot be empty");

            if (!System.Text.RegularExpressions.Regex.IsMatch(value, pattern))
                throw new ToolParameterException(
                    $"Parameter '{paramName}' value '{value}' does not match required pattern");
        }

        /// <summary>
        /// Validate that a string length is within bounds
        /// </summary>
        /// <param name="value">The string to validate</param>
        /// <param name="minLength">The minimum length (inclusive)</param>
        /// <param name="maxLength">The maximum length (inclusive)</param>
        /// <param name="paramName">The parameter name for error messages</param>
        /// <exception cref="ToolParameterException">If length is outside bounds</exception>
        public static void ValidateStringLength(string value, int minLength, int maxLength, string paramName)
        {
            if (value == null)
                throw new ToolParameterException($"Parameter '{paramName}' cannot be null");

            if (value.Length < minLength || value.Length > maxLength)
                throw new ToolParameterException(
                    $"Parameter '{paramName}' length {value.Length} is outside range [{minLength}, {maxLength}]");
        }

        /// <summary>
        /// Extract an array-of-objects parameter from Arguments.
        /// After OpenAIClient.ConvertJsonElement, arrays are List&lt;object&gt;
        /// and objects are Dictionary&lt;string, object&gt;.
        /// Returns null if parameter not present (distinguishes from empty list).
        /// </summary>
        public static List<Dictionary<string, object>> GetListOfDicts(
            Dictionary<string, object> arguments,
            string paramName)
        {
            if (arguments == null)
                throw new ArgumentNullException(nameof(arguments));

            if (!arguments.TryGetValue(paramName, out var value) || value == null)
                return null;

            if (value is List<object> list)
            {
                var result = new List<Dictionary<string, object>>(list.Count);
                foreach (var item in list)
                {
                    if (item is Dictionary<string, object> dict)
                        result.Add(dict);
                    else
                        throw new ToolParameterException(
                            $"Parameter '{paramName}' must be an array of objects, " +
                            $"but element is {item?.GetType().Name ?? "null"}");
                }
                return result;
            }

            throw new ToolParameterException(
                $"Parameter '{paramName}' must be an array, but got {value.GetType().Name}");
        }
    }
}
