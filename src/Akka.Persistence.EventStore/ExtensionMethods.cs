using System.Linq;

namespace Akka.Persistence.EventStore
{
    internal static class ExtensionMethods
    {
        public static string ToEventCase(this string str)
        {
            return string.Concat(str.Select((x, i) => i == 0 ? char.ToLower(x) : x));
        }
    }
}