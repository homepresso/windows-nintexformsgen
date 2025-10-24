namespace K2SmartObjectGenerator.Utilities
{
    public static class NameSanitizer
    {
        public static string SanitizePropertyName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;

            string sanitized = name
                .Replace(" ", "_")
                .Replace("(", "")
                .Replace(")", "")
                .Replace("\"", "")
                .Replace("+", "Plus")
                .Replace("-", "_")
                .Replace("/", "_")
                .Replace("\\", "_")
                .Replace(".", "_")
                .Replace(",", "_")
                .Replace(":", "_")
                .Replace(";", "_")
                .Replace("'", "")
                .Replace("`", "")
                .Replace("~", "")
                .Replace("!", "")
                .Replace("@", "")
                .Replace("#", "")
                .Replace("$", "")
                .Replace("%", "")
                .Replace("^", "")
                .Replace("&", "")
                .Replace("*", "")
                .Replace("=", "")
                .Replace("[", "")
                .Replace("]", "")
                .Replace("{", "")
                .Replace("}", "")
                .Replace("|", "")
                .Replace("?", "")
                .Replace("<", "")
                .Replace(">", "");

            if (sanitized.Length > 0 && char.IsDigit(sanitized[0]))
            {
                sanitized = "_" + sanitized;
            }

            while (sanitized.Contains("__"))
            {
                sanitized = sanitized.Replace("__", "_");
            }

            sanitized = sanitized.Trim('_');

            if (string.IsNullOrEmpty(sanitized))
            {
                sanitized = "Field";
            }

            return sanitized.ToUpper();
        }

        /// <summary>
        /// Sanitizes SmartObject names to match K2's internal naming rules
        /// Similar to SanitizePropertyName but preserves original casing
        /// </summary>
        public static string SanitizeSmartObjectName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return name;

            string sanitized = name
                .Replace(" ", "_")
                .Replace("(", "")
                .Replace(")", "")
                .Replace("\"", "")
                .Replace("+", "Plus")
                .Replace("-", "_")  // CRITICAL: Convert dashes to underscores like K2 does
                .Replace("/", "_")
                .Replace("\\", "_")
                .Replace(".", "_")
                .Replace(",", "_")
                .Replace(":", "_")
                .Replace(";", "_")
                .Replace("'", "")
                .Replace("`", "")
                .Replace("~", "")
                .Replace("!", "")
                .Replace("@", "")
                .Replace("#", "")
                .Replace("$", "")
                .Replace("%", "")
                .Replace("^", "")
                .Replace("&", "")
                .Replace("*", "")
                .Replace("=", "")
                .Replace("[", "")
                .Replace("]", "")
                .Replace("{", "")
                .Replace("}", "")
                .Replace("|", "")
                .Replace("?", "")
                .Replace("<", "")
                .Replace(">", "");

            if (sanitized.Length > 0 && char.IsDigit(sanitized[0]))
            {
                sanitized = "_" + sanitized;
            }

            // Collapse multiple underscores into one
            while (sanitized.Contains("__"))
            {
                sanitized = sanitized.Replace("__", "_");
            }

            // Trim leading/trailing underscores
            sanitized = sanitized.Trim('_');

            if (string.IsNullOrEmpty(sanitized))
            {
                sanitized = "SmartObject";
            }

            // Keep original casing for SmartObject names (unlike properties which are uppercased)
            return sanitized;
        }
    }
}