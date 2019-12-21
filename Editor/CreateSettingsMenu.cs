using System.IO;
using UnityEditor;
#if UNITY_EDITOR

namespace Cratesmith.Settings
{
    public static class CreateSettingsMenu 
    {
        const string formatString = "using UnityEngine;\n" +
                                    "using System;\n" +
                                    "using Cratesmith.Settings;\n" +
                                    "\n" +
                                    "public class {0} : {1}<{0}>\n" +
                                    "{{\n" +
                                    "    // Defaultable Reference type (value will return a default asset if field is null).\n"+
                                    "    [Serializable] public class Reference : SettingsReference<{0}> {{}}\n" +
                                    "    \n"+
                                    "    \n"+
                                    "    \n"+
                                    "}}\n";

        [MenuItem("Assets/Create/C# SettingsAsset", false, 100)] 
        private static void ShowCreateSettingsClassDialog()
        {
            var baseTypeName = ScriptAssetUtil.TrimGenericsFromType(typeof(SettingsAsset<>).Name);
            ScriptAssetUtil.ShowCreateScriptDialog(typeof(SettingsAsset<>), formatString, baseTypeName);
        }

        public static void CreateSettingsScript(string filename)
        {
            var baseTypeName = ScriptAssetUtil.TrimGenericsFromType(typeof(SettingsAsset<>).Name);
            var className = Path.GetFileNameWithoutExtension(filename);
            ScriptAssetUtil.CreateScript(filename, formatString, className, baseTypeName);
        }
    }
}
#endif