namespace SitecoreSerialisationConverter
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Text.RegularExpressions;
    using System.Xml.Linq;
    using Extensions;
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
            XNamespace xmlNamespace = "http://schemas.microsoft.com/developer/msbuild/2003";

            var projectName = project.Descendants(xmlNamespace + "RootNamespace").Select(x => x.Value).FirstOrDefault();

            SerializationModuleConfiguration newConfigModule = new SerializationModuleConfiguration()
            {
                Description = Settings.ProjectDescription,
                Namespace = projectName,
                Items = new SerializationModuleConfigurationItems()
                {
                    Includes = new List<FilesystemTreeSpec>()
                }
            };

            ParseItems(project, xmlNamespace, newConfigModule);

            ParseRoles(project, xmlNamespace, newConfigModule);

            if (newConfigModule.Items.Includes.Any() || newConfigModule.Roles.Any())
            {
                if (useRelativeSavePath)
                {
                    savePath = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(projectPath), @relativeSavePath));
                }

                WriteNewConfig(savePath, newConfigModule);
            }

        }

        private static void ParseRoles(XDocument project, XNamespace msbuild, SerializationModuleConfiguration newConfigModule)
        {
            var roles = new List<RolePredicateItem>();

            foreach (var sitecoreRole in project.Descendants(msbuild + "SitecoreRole"))
            {
                var includePathSplit = sitecoreRole.Attribute("Include")?.Value.Split('\\');

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
        }

        private static void ParseItems(XDocument project,
            XNamespace xmlNamespace,
            SerializationModuleConfiguration newConfigModule)
        {
            SitecoreDb database = (SitecoreDb)Enum.Parse(typeof(SitecoreDb),
                project.Descendants(xmlNamespace + "SitecoreDatabase").Select(x => x.Value).FirstOrDefault() ?? "master");

            var items = project.Descendants(xmlNamespace + "SitecoreItem")
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

            var dictionaryItems = ItemTreeService.GetFilteredItemDictionary(items);

            foreach (var sitecoreItem in dictionaryItems)
            {
                if (!sitecoreItem.Value.IsParentSyncEnabled)
                    AddItem(database, newConfigModule, sitecoreItem.Value);
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

        public static void AddItem(SitecoreDb database, SerializationModuleConfiguration newConfigModule, SitecoreItem item)
        {
            var includePath = PathAlias.Remove(item.Include, AliasList);

            var path = SafePath.Get(includePath);

            if (!IsIgnoredRoute(database, path))
            {
                FilesystemTreeSpec newSpec = new FilesystemTreeSpec
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
        }

        private static void WriteNewConfig(string savePath, SerializationModuleConfiguration moduleConfiguration)
        {
            var path = Path.Combine(savePath, moduleConfiguration.Namespace + ".module.json");
            using (FileStream outputStream = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                using (StreamWriter textWriter = new StreamWriter(outputStream))
                {
                    JsonSerializer.Create(SerializerSettings).Serialize(textWriter, moduleConfiguration);
                }
            }

            Console.WriteLine(path);
        }

        private static readonly JsonSerializerSettings SerializerSettings = new()
        {
            Converters = new List<JsonConverter>
            {
                new JsonItemPathConverter(),
                new JsonItemPathMatchConverter(),
                new FilesystemTreeSpecRuleConverter(),
                new StringEnumConverter(new CamelCaseNamingStrategy())
            },
            Formatting = Formatting.Indented,
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
