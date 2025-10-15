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
    }
}