using System.Collections.Generic;

namespace SitecoreSerialisationConverter.Service
{
    using System.Linq;
    using SitecoreSerialisationConverter.Models;

    public static class ItemTreeService
    {
        public static ItemTree GetFoldersFormStrings(List<SitecoreItem> items)
        {
            var itemTree = new ItemTree();

            var itemsByPath = new Dictionary<string, SitecoreItem>
            {
                {
                    "sitecore\\",
                    new SitecoreItem
                    {
                        Include = "sitecore\\",
                        ChildItemSynchronization = ChildSynchronizationType.NoChildSynchronization,
                        ItemDeployment = ItemDeploymentType.NeverDeploy,
                        Children = new List<SitecoreItem>(),
                        Parent = new SitecoreRootItem()
                    }
                }
            };

            foreach (var item in items)
            {
                var lastSlashPosition = item.Include.LastIndexOf("\\");
                var parentFolderPath = item.Include.Substring(0, lastSlashPosition + 1);
                
                var isItemExisting = itemsByPath.TryGetValue(parentFolderPath, out var existingItem);

                if (parentFolderPath == "sitecore\\")
                {
                    itemTree.RootItem = existingItem;
                }

                if (!isItemExisting)
                {
                    itemTree.UnstructuredItems.Add(item);
                }
                else
                {
                    item.Parent = existingItem;
                    existingItem.Children.Add(item);
                    itemsByPath.Add(item.Include.Replace(".item", "\\"), item);
                }
                
            }
            return itemTree;
        }

        public static Dictionary<string, SitecoreItem> GetFilteredItemDictionary(IList<SitecoreItem> items)
        {
            var response = new Dictionary<string, SitecoreItem>();

            foreach (var sitecoreItem in items)
            {
                var parentPath = sitecoreItem.Include.Substring(0, sitecoreItem.Include.LastIndexOf("\\") + 1);
                //Check if parent item is in dictionary
                var parentItemExists = response.TryGetValue(parentPath, out var parentItem);
                if (parentItemExists && parentItem.ChildItemSynchronization != ChildSynchronizationType.NoChildSynchronization)
                {
                    sitecoreItem.IsParentSyncEnabled = true;
                }
                else
                {
                    sitecoreItem.IsParentSyncEnabled = false;
                }

                response.Add(sitecoreItem.Include.Replace(".item","\\"), sitecoreItem);
            }

            return response;
        }
    }
}
