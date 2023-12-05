using System.Collections.Generic;

namespace SitecoreSerialisationConverter.Service
{
    using SitecoreSerialisationConverter.Models;

    internal static class ItemTreeService
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
    }
}
