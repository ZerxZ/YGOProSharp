using System.Collections.Specialized;

namespace WindBot;

public static class QueryStringParser
{
    public static NameValueCollection ParseQueryString(string query)
    {
        NameValueCollection result = new NameValueCollection();
        if (!string.IsNullOrEmpty(query))
        {
            string[] pairs = query.Split('&');
            foreach (string pair in pairs)
            {
                if (pair.Contains("="))
                {
                    string[] parts = pair.Split('=');
                    string key = Uri.UnescapeDataString(parts[0]);
                    string value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
                    result[key] = value;
                }
            }
        }
        return result;
    }
}
