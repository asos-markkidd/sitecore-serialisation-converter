using SitecoreSerialisationConverter.Models;
using System.Collections.Generic;

namespace SitecoreSerialisationConverter.Commands
{
    public static class PathAlias
    {
        public static string Remove(string includePath, List<AliasItem> AliasList)
        {
            if (AliasList.Count > 0)
            {
                foreach (var item in AliasList)
                {
                    if (includePath.Contains($@"\{item.AliasName}\") || includePath.Contains($@"\{item.AliasName}."))
                    {
                        includePath = includePath.Replace($@"\{item.AliasName}\", $@"\{item.SitecoreName}\").Replace($@"\{item.AliasName}.", $@"\{item.SitecoreName}.");
                    }
                }
            }

            return includePath;
        }
    }
}
