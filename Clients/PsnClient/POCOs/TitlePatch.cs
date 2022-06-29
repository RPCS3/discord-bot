using System;
using System.Xml.Serialization;

namespace PsnClient.POCOs;
#nullable disable
    
[XmlRoot("titlepatch")]
public class TitlePatch
{
    [XmlAttribute("titleid")]
    public string TitleId { get; set; }
    [XmlAttribute("status")]
    public string Status { get; set; }
    [XmlElement("tag")]
    public TitlePatchTag Tag { get; set; }
    [XmlIgnore]
    public DateTime? OfflineCacheTimestamp { get; set; }
}

public class TitlePatchTag
{
    [XmlAttribute("name")]
    public string Name { get; set; }
    //no root element
    [XmlElement("package")]
    public TitlePatchPackage[] Packages { get; set; }
}

public class TitlePatchPackage
{
    [XmlAttribute("version")]
    public string Version { get; set; }
    [XmlAttribute("size")]
    public long Size { get; set; }
    [XmlAttribute("sha1sum")]
    public string Sha1Sum { get; set; }
    [XmlAttribute("url")]
    public string Url { get; set; }
    [XmlAttribute("ps3_system_ver")]
    public string Ps3SystemVer { get; set; }
    [XmlElement("paramsfo")]
    public TitlePatchParamSfo ParamSfo { get; set; }
}

public class TitlePatchParamSfo
{
    [XmlElement("TITLE")]
    public string Title { get; set; }
}
    
#nullable restore