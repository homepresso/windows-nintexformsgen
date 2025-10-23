using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace FormGenerator
{
    /// <summary>
    /// Compatibility helpers for .NET Framework 4.8
    /// </summary>
    public static class NetFrameworkCompatibility
    {
        // File.WriteAllTextAsync doesn't exist in .NET Framework
        public static Task WriteAllTextAsync(string path, string contents)
        {
            return Task.Run(() => File.WriteAllText(path, contents));
        }

        // HashCode doesn't exist in .NET Framework
        public static int CombineHashCodes(params object[] objects)
        {
            unchecked
            {
                int hash = 17;
                foreach (var obj in objects)
                {
                    hash = hash * 31 + (obj?.GetHashCode() ?? 0);
                }
                return hash;
            }
        }
    }
}
