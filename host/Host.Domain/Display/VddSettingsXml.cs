using System.Xml.Linq;

namespace ScreenPortal.Host.Domain.Display;

/// <summary>Edita o vdd_settings.xml do Virtual Display Driver (a lista de resoluções é global a todos os monitores).</summary>
public static class VddSettingsXml
{
    public static string EnsureResolution(string xml, int width, int height, int refreshRate)
    {
        var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        var resolutions = doc.Root!.Element("resolutions") ?? AddElement(doc.Root!, "resolutions");
        bool exists = resolutions.Elements("resolution").Any(r =>
            (int?)r.Element("width") == width && (int?)r.Element("height") == height && (int?)r.Element("refresh_rate") == refreshRate);
        if (exists) return xml;

        resolutions.Add(new XElement("resolution",
            new XElement("width", width), new XElement("height", height), new XElement("refresh_rate", refreshRate)));
        return doc.Declaration + doc.ToString(SaveOptions.DisableFormatting);
    }

    public static int ReadMonitorCount(string xml) =>
        (int?)XDocument.Parse(xml).Root!.Element("monitors")?.Element("count") ?? 1;

    private static XElement AddElement(XElement parent, string name)
    {
        var element = new XElement(name);
        parent.Add(element);
        return element;
    }
}
