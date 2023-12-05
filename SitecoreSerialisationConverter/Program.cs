﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using Sitecore.DevEx.Serialization;
using Sitecore.DevEx.Serialization.Client;
using Sitecore.DevEx.Serialization.Client.Configuration;
using Sitecore.DevEx.Serialization.Client.Datasources.Filesystem.Configuration;
using Sitecore.DevEx.Serialization.Models;
using Sitecore.DevEx.Serialization.Models.Roles;
using SitecoreSerialisationConverter.Commands;
using SitecoreSerialisationConverter.Models;

namespace SitecoreSerialisationConverter
{
    using Extensions;
    using SitecoreSerialisationConverter.Service;

    public class Program
    {
        public static string AppSettingsJsonFile = "appsettings.json";
        public static Settings Settings;
        public static List<AliasItem> AliasList;

        public static void Main(string[] args)
        {
            IConfiguration config = new ConfigurationBuilder()
                .AddJsonFile(AppSettingsJsonFile)
                .AddEnvironmentVariables()
                .Build();

            Settings = config.GetRequiredSection("Settings").Get<Settings>();

            var solutionFolder = Settings.SolutionFolder;
            var tdsFiles = Directory.GetFiles(solutionFolder, "*.scproj", SearchOption.AllDirectories);
            var savePath = Settings.SavePath;
            bool useRelativeSavePath = Settings.UseRelativeSavePath;
            var relativeSavePath = Settings.RelativeSavePath;

            if (!Directory.Exists(savePath))
            {
                Directory.CreateDirectory(savePath);
            }

            foreach (var file in tdsFiles)
            {
                ConvertSerialisationFile(file, savePath, useRelativeSavePath, relativeSavePath);
            }
        }

        private static void ConvertSerialisationFile(string projectPath, string savePath, bool useRelativeSavePath, string relativeSavePath)
        {
            XDocument project = XDocument.Load(projectPath);
            XNamespace msbuild = "http://schemas.microsoft.com/developer/msbuild/2003";

            if (project != null)
            {
                string projectName = project.Descendants(msbuild + "RootNamespace").Select(x => x.Value).FirstOrDefault();
                SitecoreDb database = (SitecoreDb)Enum.Parse(typeof(SitecoreDb),
                    project.Descendants(msbuild + "SitecoreDatabase").Select(x => x.Value).FirstOrDefault() ?? "master");

                SerializationModuleConfiguration newConfigModule = new SerializationModuleConfiguration()
                {
                    Description = Settings.ProjectDescription,
                    Namespace = projectName,
                    Items = new SerializationModuleConfigurationItems()
                    {
                        Includes = new List<FilesystemTreeSpec>()
                    }
                };

                var roles = new List<RolePredicateItem>();

                var ignoreSyncChildren = false;
                var ignoreDirectSyncChildren = false;

                var items = project.Descendants(msbuild + "SitecoreItem")
                    .Select(x => x.DeserializeSitecoreItem<SitecoreItem>())
                    .ToList();

                AliasList = items
                    .Where(a => !string.IsNullOrWhiteSpace(a.SitecoreName))
                    .Select(s => new AliasItem
                    {
                        AliasName = Path.GetFileNameWithoutExtension(s.Include),
                        SitecoreName = s.SitecoreName
                    })
                    .ToList();
                
                //foreach (var sitecoreItem in project.Descendants(msbuild + "SitecoreItem"))
                //{
                //    var item = sitecoreItem.DeserializeSitecoreItem<SitecoreItem>();

                //    if (!string.IsNullOrEmpty(item.SitecoreName))
                //    {
                //        AliasItem aliasItem = new AliasItem()
                //        {
                //            AliasName = Path.GetFileNameWithoutExtension(item.Include),
                //            SitecoreName = item.SitecoreName
                //        };

                //        AliasList.Add(aliasItem);
                //    }

                //    RenderItem(database, newConfigModule, ref ignoreSyncChildren, ref ignoreDirectSyncChildren, item.Include, item.ItemDeployment, item.ChildItemSynchronization);
                //}

                ParseItemDictionary(items, database, newConfigModule);

                foreach (var sitecoreRole in project.Descendants(msbuild + "SitecoreRole"))
                {
                    var includePathSplit = sitecoreRole.Attribute("Include")?.Value?.Split('\\');

                    if (includePathSplit?.Length == 3)
                    {
                        var domain = includePathSplit[1];
                        var roleName = includePathSplit[2];

                        if (!string.IsNullOrEmpty(domain) && !string.IsNullOrEmpty(roleName))
                        {
                            roles.Add(new RolePredicateItem()
                            {
                                Domain = domain,
                                Pattern = roleName.Replace(".role", string.Empty)
                            });
                        }
                    }
                }

                if (roles.Count > 0)
                {
                    newConfigModule.Roles = roles;
                }

                if (newConfigModule.Items.Includes.Any() || newConfigModule.Roles.Any())
                {
                    if (useRelativeSavePath)
                    {
                        savePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectPath), @relativeSavePath));
                    }

                    WriteNewConfig(savePath, newConfigModule);
                }
            }
        }
        
        private static void ParseItemDictionary(IList<SitecoreItem> items, 
            SitecoreDb database, 
            SerializationModuleConfiguration newConfigModule)
        {
            var dictionaryItems = ItemTreeService.GetFilteredItemDictionary(items);

            foreach (var sitecoreItem in dictionaryItems)
            {
                if(!sitecoreItem.Value.IsParentSyncEnabled)
                    AddItem(database, newConfigModule, sitecoreItem.Value);
            }
        }

        private static void RenderItem(SitecoreDb database, SerializationModuleConfiguration newConfigModule, ref bool ignoreSyncChildren, ref bool ignoreDirectSyncChildren, string includePath, ItemDeploymentType deploymentType, ChildSynchronizationType childSynchronisation)
        {
            switch (childSynchronisation)
            {
                case ChildSynchronizationType.NoChildSynchronization:
                {
                    var path = SafePath.Get(includePath);

                    if (!IsIgnoredRoute(database, path))
                    {
                        AddItem(database, newConfigModule, includePath, deploymentType, childSynchronisation);
                    }

                    break;
                }
                case ChildSynchronizationType.KeepAllChildrenSynchronized when !ignoreSyncChildren:
                    ignoreSyncChildren = true;

                    AddItem(database, newConfigModule, includePath, deploymentType, childSynchronisation);
                    break;

            }

            if (childSynchronisation != ChildSynchronizationType.KeepAllChildrenSynchronized)
            {
                ignoreSyncChildren = false;
            }

            if (childSynchronisation == ChildSynchronizationType.KeepDirectDescendantsSynchronized && !ignoreDirectSyncChildren)
            {
                ignoreDirectSyncChildren = true;

                AddItem(database, newConfigModule, includePath, deploymentType, childSynchronisation);
            }

            if (childSynchronisation != ChildSynchronizationType.KeepDirectDescendantsSynchronized)
            {
                ignoreDirectSyncChildren = false;
            }
        }

        private static bool IsIgnoredRoute(SitecoreDb database, string path)
        {
            IEnumerable<string> ignoredPaths;
            switch (database)
            {
                case SitecoreDb.core:
                    ignoredPaths = Settings.IgnoredRoutes.Core.Where(x => Regex.IsMatch(x, path, RegexOptions.IgnoreCase));
                    break;

                case SitecoreDb.master:
                    ignoredPaths = Settings.IgnoredRoutes.Master.Where(x => Regex.IsMatch(x, path, RegexOptions.IgnoreCase));
                    break;

                default:
                    ignoredPaths = new List<string>();
                    break;
            }

            return ignoredPaths.Any();
        }

        public static void AddItem(SitecoreDb database, SerializationModuleConfiguration newConfigModule, string includePath, ItemDeploymentType deploymentType, ChildSynchronizationType childSynchronisation)
        {
            includePath = PathAlias.Remove(includePath, AliasList);

            FilesystemTreeSpec newSpec = new FilesystemTreeSpec()
            {
                Name = SafeName.Get(includePath),
                Path = ItemPath.FromPathString(SafePath.Get(includePath)),
                AllowedPushOperations = PushOperation.Get(deploymentType),
                Scope = ProjectedScope.Get(childSynchronisation)
            };
            
            //if it's not default then set it.
            if (database != SitecoreDb.master)
            {
                newSpec.Database = database.ToString();
            }

            //set defaults
            newConfigModule.Items.Includes.Add(newSpec);
        }

        public static void AddItem(SitecoreDb database, SerializationModuleConfiguration newConfigModule, SitecoreItem item)
        {
            var includePath = PathAlias.Remove(item.Include, AliasList);

            FilesystemTreeSpec newSpec = new FilesystemTreeSpec()
            {
                Name = SafeName.Get(includePath),
                Path = ItemPath.FromPathString(SafePath.Get(includePath)),
                AllowedPushOperations = PushOperation.Get(item.ItemDeployment),
                Scope = ProjectedScope.Get(item.ChildItemSynchronization)
            };

            //if it's not default then set it.
            if (database != SitecoreDb.master)
            {
                newSpec.Database = database.ToString();
            }

            //set defaults
            newConfigModule.Items.Includes.Add(newSpec);
        }

        private static void WriteNewConfig(string savePath, SerializationModuleConfiguration moduleConfiguration)
        {
            var path = Path.Combine(savePath, moduleConfiguration.Namespace + ".module.json");
            using (FileStream outputStream = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                using (StreamWriter textWriter = new StreamWriter(outputStream))
                {
                    JsonSerializer.Create(_serializerSettings).Serialize(textWriter, moduleConfiguration);
                }
            }

            Console.WriteLine(path);
        }

        private static readonly JsonSerializerSettings _serializerSettings = new JsonSerializerSettings
        {
            Converters = new List<JsonConverter>
            {
                new JsonItemPathConverter(),
                new JsonItemPathMatchConverter(),
                new FilesystemTreeSpecRuleConverter(),
                new StringEnumConverter(new CamelCaseNamingStrategy())
            },
            Formatting = Newtonsoft.Json.Formatting.Indented,
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            //if we do want to leave out the defaults.
            //DefaultValueHandling = DefaultValueHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };
    }

    public class FilesystemTreeSpecRuleConverter : JsonConverter<TreeSpecRule>
    {
        public override bool CanWrite => false;

        public override void WriteJson(JsonWriter writer, TreeSpecRule value, JsonSerializer serializer)
        {
            throw new NotImplementedException();
        }

        public override TreeSpecRule ReadJson(JsonReader reader, Type objectType, TreeSpecRule existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            FilesystemTreeSpecRule filesystemTreeSpecRule = new FilesystemTreeSpecRule();
            serializer.Populate(reader, filesystemTreeSpecRule);
            return filesystemTreeSpecRule;
        }
    }
}
