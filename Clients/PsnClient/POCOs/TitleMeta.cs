using System.Xml.Serialization;

namespace PsnClient.POCOs;
#nullable disable
    
[XmlRoot("title-info")]
public class TitleMeta
{
    [XmlAttribute("rev")]
    public int Rev { get; set; }
    [XmlElement("id")]
    public string Id { get; set; }
    [XmlElement("console")]
    public string Console { get; set; }
    [XmlElement("media-type")]
    public string MediaType { get; set; }
    [XmlElement("name")]
    public string Name { get; set; }
    [XmlElement("parental-level")]
    public int ParentalLevel { get; set; }
    [XmlElement("icon")]
    public TitleIcon Icon { get; set; }
    [XmlElement("resolution")]
    public string Resolution { get; set; }
    [XmlElement("sound-format")]
    public string SoundFormat { get; set; }
}

public class TitleIcon
{
    [XmlAttribute("type")]
    public string Type { get; set; }
    [XmlText]
    public string Url { get; set; }
}
    
#nullable restore