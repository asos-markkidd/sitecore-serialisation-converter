using System.Collections.Generic;

namespace SitecoreSerialisationConverter.Models
{
    public class ItemTree
    {
        public ItemTree()
        {
            UnstructuredItems = new List<SitecoreItem>();
        }

        public SitecoreItem RootItem { get; set; }

        public IList<SitecoreItem> UnstructuredItems { get; set; }

    }
}
