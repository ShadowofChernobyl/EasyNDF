namespace EasyNdf.Engine.Rules;

/// <summary>
/// GUIDs for cloned descriptors that are the same on every export: they are worked out from the clone's name, so
/// exporting a mod twice does not change its ids.
/// </summary>
/// <remarks>
/// The hash is cyrb128, as in the HTML app. It must stay bit-for-bit the same, or mods would get new ids when moving
/// from the HTML app to this one. The casts copy JavaScript's 32-bit arithmetic (Math.imul, >>> and ^).
/// </remarks>
public static class StableGuid
{
    /// <summary>A GUID in NDF form, GUID:{xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx}, worked out from a text.</summary>
    public static string From(string text)
    {
        unchecked
        {
            int h1 = 1779033703, h2 = (int)3144134277, h3 = 1013904242, h4 = (int)2773480762;
            foreach (char c in text)
            {
                int k = c;
                h1 = h2 ^ (h1 ^ k) * 597399067;
                h2 = h3 ^ (h2 ^ k) * (int)2869860233;
                h3 = h4 ^ (h3 ^ k) * 951274213;
                h4 = h1 ^ (h4 ^ k) * (int)2716044179;
            }
            h1 = (h3 ^ (int)((uint)h1 >> 18)) * 597399067;
            h2 = (h4 ^ (int)((uint)h2 >> 22)) * (int)2869860233;
            h3 = (h1 ^ (int)((uint)h3 >> 17)) * 951274213;
            h4 = (h2 ^ (int)((uint)h4 >> 19)) * (int)2716044179;

            string hex = string.Concat(new[] { h1 ^ h2 ^ h3 ^ h4, h2 ^ h1, h3 ^ h1, h4 ^ h1 }.Select(v => ((uint)v).ToString("x8")));
            return "GUID:{" + hex[..8] + "-" + hex[8..12] + "-" + hex[12..16] + "-" + hex[16..20] + "-" + hex[20..32] + "}";
        }
    }
}
