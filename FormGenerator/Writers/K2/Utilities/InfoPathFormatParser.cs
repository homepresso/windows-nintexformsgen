using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace K2SmartObjectGenerator.Utilities
{
    /// <summary>
    /// Parses InfoPath formatting properties and converts them to K2 format specifications
    /// </summary>
    public static class InfoPathFormatParser
    {
        public class FormatInfo
        {
            public string Type { get; set; }
            public string Pattern { get; set; }
            public int? DecimalPlaces { get; set; }
            public int? NegativeOrder { get; set; }
            public string DateFormat { get; set; }
            public string CurrencySymbol { get; set; }
            public string Culture { get; set; } = "en-US";
        }

        /// <summary>
        /// Parses InfoPath datafmt string and returns format information
        /// </summary>
        /// <param name="datafmt">InfoPath datafmt string like "\"number\",\"numDigits:2;negativeOrder:1;\""</param>
        /// <param name="boundProp">InfoPath boundProp like "xd:num"</param>
        /// <returns>Parsed format information</returns>
        public static FormatInfo ParseDataFormat(string datafmt, string boundProp = null)
        {
            if (string.IsNullOrEmpty(datafmt))
                return null;

            var formatInfo = new FormatInfo();

            try
            {
                // Remove outer quotes and split by comma
                string cleanDatafmt = datafmt.Trim('"');
                string[] parts = cleanDatafmt.Split(',');

                if (parts.Length >= 1)
                {
                    // Extract the format type (number, date, etc.)
                    string formatType = parts[0].Trim('"');
                    formatInfo.Type = formatType;

                    // Parse format-specific properties
                    if (parts.Length >= 2)
                    {
                        string properties = parts[1].Trim('"');

                        switch (formatType.ToLower())
                        {
                            case "number":
                                ParseNumberFormat(properties, formatInfo);
                                break;
                            case "date":
                                ParseDateFormat(properties, formatInfo);
                                break;
                            case "currency":
                                ParseCurrencyFormat(properties, formatInfo);
                                break;
                        }
                    }
                }

                // Apply boundProp information
                if (!string.IsNullOrEmpty(boundProp))
                {
                    ApplyBoundPropInfo(boundProp, formatInfo);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to parse InfoPath format '{datafmt}': {ex.Message}");
                return null;
            }

            return formatInfo;
        }

        private static void ParseNumberFormat(string properties, FormatInfo formatInfo)
        {
            // Parse properties like "numDigits:2;negativeOrder:1;"
            var matches = Regex.Matches(properties, @"(\w+):([^;]+);?");

            foreach (Match match in matches)
            {
                string property = match.Groups[1].Value;
                string value = match.Groups[2].Value;

                switch (property.ToLower())
                {
                    case "numdigits":
                        if (value.ToLower() == "auto")
                        {
                            formatInfo.DecimalPlaces = null; // Auto precision
                        }
                        else if (int.TryParse(value, out int digits))
                        {
                            formatInfo.DecimalPlaces = digits;
                        }
                        break;
                    case "negativeorder":
                        if (int.TryParse(value, out int order))
                        {
                            formatInfo.NegativeOrder = order;
                        }
                        break;
                }
            }
        }

        private static void ParseDateFormat(string properties, FormatInfo formatInfo)
        {
            // Parse properties like "dateFormat:Short Date;"
            var matches = Regex.Matches(properties, @"(\w+):([^;]+);?");

            foreach (Match match in matches)
            {
                string property = match.Groups[1].Value;
                string value = match.Groups[2].Value;

                switch (property.ToLower())
                {
                    case "dateformat":
                        formatInfo.DateFormat = value;
                        break;
                }
            }
        }

        private static void ParseCurrencyFormat(string properties, FormatInfo formatInfo)
        {
            // Parse currency-specific properties
            var matches = Regex.Matches(properties, @"(\w+):([^;]+);?");

            foreach (Match match in matches)
            {
                string property = match.Groups[1].Value;
                string value = match.Groups[2].Value;

                switch (property.ToLower())
                {
                    case "currencysymbol":
                        formatInfo.CurrencySymbol = value;
                        break;
                    case "numdigits":
                        if (int.TryParse(value, out int digits))
                        {
                            formatInfo.DecimalPlaces = digits;
                        }
                        break;
                    case "negativeorder":
                        if (int.TryParse(value, out int order))
                        {
                            formatInfo.NegativeOrder = order;
                        }
                        break;
                }
            }
        }

        private static void ApplyBoundPropInfo(string boundProp, FormatInfo formatInfo)
        {
            // Apply information from boundProp like "xd:num"
            if (boundProp.StartsWith("xd:"))
            {
                string dataType = boundProp.Substring(3);
                switch (dataType.ToLower())
                {
                    case "num":
                        if (string.IsNullOrEmpty(formatInfo.Type))
                        {
                            formatInfo.Type = "number";
                        }
                        break;
                    case "date":
                        if (string.IsNullOrEmpty(formatInfo.Type))
                        {
                            formatInfo.Type = "date";
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// Converts InfoPath format information to K2 format pattern string
        /// </summary>
        /// <param name="formatInfo">Parsed format information</param>
        /// <returns>K2 format pattern (e.g., "F2", "d", "C2")</returns>
        public static string ToK2FormatPattern(FormatInfo formatInfo)
        {
            if (formatInfo == null)
                return null;

            switch (formatInfo.Type?.ToLower())
            {
                case "number":
                    if (formatInfo.DecimalPlaces.HasValue)
                    {
                        return $"F{formatInfo.DecimalPlaces}"; // F2 = 2 decimal places
                    }
                    else
                    {
                        return "N2"; // Number format with 2 decimal places for auto precision (more user-friendly than G)
                    }

                case "currency":
                    if (formatInfo.DecimalPlaces.HasValue)
                    {
                        return $"C{formatInfo.DecimalPlaces}"; // C2 = currency with 2 decimal places
                    }
                    else
                    {
                        return "C"; // Default currency format
                    }

                case "date":
                    switch (formatInfo.DateFormat?.ToLower())
                    {
                        case "short date":
                            return "d"; // Short date pattern
                        case "long date":
                            return "D"; // Long date pattern
                        case "short time":
                            return "t"; // Short time pattern
                        case "long time":
                            return "T"; // Long time pattern
                        default:
                            return "d"; // Default to short date
                    }

                default:
                    return null;
            }
        }

        /// <summary>
        /// Gets the K2 format type for XML generation
        /// </summary>
        /// <param name="formatInfo">Parsed format information</param>
        /// <returns>K2 format type (Number, Currency, Date, etc.)</returns>
        public static string ToK2FormatType(FormatInfo formatInfo)
        {
            if (formatInfo == null)
                return null;

            switch (formatInfo.Type?.ToLower())
            {
                case "number":
                    return "Number";
                case "currency":
                    return "Currency";
                case "date":
                    return "Date";
                case "percent":
                    return "Percent";
                default:
                    return "Number"; // Default fallback
            }
        }

        /// <summary>
        /// Gets the negative pattern number for K2
        /// </summary>
        /// <param name="formatInfo">Parsed format information</param>
        /// <returns>Negative pattern string for K2</returns>
        public static string ToK2NegativePattern(FormatInfo formatInfo)
        {
            if (formatInfo?.NegativeOrder == null)
                return "";

            // InfoPath negative orders map to different display patterns
            // This is a simplified mapping - may need adjustment based on actual InfoPath behavior
            switch (formatInfo.NegativeOrder)
            {
                case 0:
                    return "(1.1)"; // Parentheses
                case 1:
                    return "-1.1"; // Leading minus
                case 2:
                    return "- 1.1"; // Leading minus with space
                case 3:
                    return "1.1-"; // Trailing minus
                default:
                    return "-1.1"; // Default to leading minus
            }
        }
    }
}