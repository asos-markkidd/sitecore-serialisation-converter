﻿using System.Xml.Serialization;

namespace SitecoreSerialisationConverter.Models
{
    using System.Collections.Generic;

    public class SitecoreRootItem : SitecoreItem
    {

    }

    [XmlRoot(ElementName = "SitecoreItem")]
    public class SitecoreItem
    {
        public SitecoreItem()
        {
            Children = new List<SitecoreItem>();
        }

        [XmlAttribute(AttributeName = "Include")]
        public string Include { get; set; }

        public string SitecoreName { get; set; }
        public ItemDeploymentType ItemDeployment { get; set; }
        public ChildSynchronizationType ChildItemSynchronization { get; set; }

        [XmlIgnore]
        public SitecoreItem Parent { get; set; }

        [XmlIgnore]
        public IList<SitecoreItem> Children { get; set; }

    }
}
