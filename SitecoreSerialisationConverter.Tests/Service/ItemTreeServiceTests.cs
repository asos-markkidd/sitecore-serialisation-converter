namespace SitecoreSerialisationConverter.Tests.Service
{
    using System.Reflection;
    using System.Xml.Linq;
    using SitecoreSerialisationConverter.Extensions;
    using SitecoreSerialisationConverter.Models;
    using SitecoreSerialisationConverter.Service;

    [TestFixture]
    public class ItemTreeServiceTests
    {
        [TestCase("\\ApprovalTestSrc\\Core\\Test.Data.Core.scproj")]
        [TestCase("\\ApprovalTestSrc\\Master\\Test.Data.Master.scproj")]
        public void ItemTreeShouldHaveAllItems(string projectPath)
        {
            var execPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            XDocument project = XDocument.Load(execPath + projectPath);
            XNamespace msbuild = "http://schemas.microsoft.com/developer/msbuild/2003";

            if (project == null)
                Assert.Fail("Project file could not be loaded");

            else
            {
                var items = project.Descendants(msbuild + "SitecoreItem")
                    .Select(x => x.DeserializeSitecoreItem<SitecoreItem>())
                    .ToList();

                var responseTree = ItemTreeService.GetFoldersFormStrings(items);

                Assert.IsFalse(responseTree.UnstructuredItems.Any());

                Assert.AreEqual(items.Count(), responseTree.RootItem.Flatten().Count() - 1);
            }
        }

        [TestCase("\\ApprovalTestSrc\\Core\\Test.Data.Core.scproj")]
        [TestCase("\\ApprovalTestSrc\\Master\\Test.Data.Master.scproj")]
        public void DictionaryShouldSetIsParentSyncEnabled(string projectPath)
        {
            var execPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            XDocument project = XDocument.Load(execPath + projectPath);
            XNamespace msbuild = "http://schemas.microsoft.com/developer/msbuild/2003";


            var items = project.Descendants(msbuild + "SitecoreItem")
                .Select(x => x.DeserializeSitecoreItem<SitecoreItem>())
                .ToList();

            var responseTree = ItemTreeService.GetFilteredItemDictionary(items);

            Assert.IsNotEmpty(responseTree);

            foreach (var isParentSyncEnabledItem in responseTree.Values.Where(x => x.IsParentSyncEnabled))
            {
                Assert.IsNotNull(isParentSyncEnabledItem);

                var lastIndexOfParentPath = isParentSyncEnabledItem.Include.LastIndexOf("\\");
                var parentPath = isParentSyncEnabledItem.Include.Substring(0, lastIndexOfParentPath + 1);
                var isParentItemPresent = responseTree.TryGetValue(parentPath, out var parentItem);

                Assert.IsTrue(isParentItemPresent);
                Assert.IsNotNull(parentItem);
                Assert.IsTrue(parentItem.ChildItemSynchronization != ChildSynchronizationType.NoChildSynchronization);
            }

        }
    }
}
