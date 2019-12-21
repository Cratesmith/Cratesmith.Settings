#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Experimental.SceneManagement;
using Cratesmith.PopoutInspector;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Cratesmith.Settings
{
    [CustomPropertyDrawer(typeof(SettingsReferenceBase), true)]
    public class SettingsReferenceDrawer : PropertyDrawer
    {
        class CloneTypes
        {
            public System.Type[] types;
            public string[] names;
        }
        private static readonly Dictionary<System.Type, CloneTypes> s_cloneTypes = new Dictionary<Type, CloneTypes>();

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            const int CLONE_BUTTON_WIDTH = 50;
            const int APPLYDEFAULT_BUTTON_WIDTH = 50;
            const int ALL_BUTTONS_WIDTH = CLONE_BUTTON_WIDTH + APPLYDEFAULT_BUTTON_WIDTH;

            var propertyFieldRect = new Rect(position.x, position.y, position.width - ALL_BUTTONS_WIDTH, position.height);
            var cloneButtonRect = new Rect(propertyFieldRect.xMax + 1, position.y + 1, CLONE_BUTTON_WIDTH - 2, position.height - 2);
            var applyButtonRect = new Rect(cloneButtonRect.xMax + 1, position.y + 1, APPLYDEFAULT_BUTTON_WIDTH - 2, position.height - 2);

            var overrideProperty = property.FindPropertyRelative("overrideValue");
            var defaultAsset = SettingsReferenceBase.GetDefaultAssetForReferenceType(property.GetSerializedPropertyType());

            OnGUI_DrawPropertyField(position, label, propertyFieldRect, overrideProperty);
            OnGUI_DrawAssetControlButtons(cloneButtonRect, applyButtonRect, property, overrideProperty, defaultAsset);
            OnGUI_DrawOverridePopout(position, overrideProperty, defaultAsset);
        }

        private static void OnGUI_DrawAssetControlButtons(Rect cloneButtonRect, Rect applyButtonRect, SerializedProperty property, SerializedProperty overrideProperty, Object defaultAsset)
        {
            using (var scope = new EditorGUI.IndentLevelScope(-EditorGUI.indentLevel))
            {
                CloneTypes cloneTypes = null;
                var defaultType = defaultAsset.GetType();
                if (!s_cloneTypes.TryGetValue(defaultType, out cloneTypes))
                {
                    var _types = new System.Type[] {null}.Concat(System.AppDomain.CurrentDomain.GetAssemblies()
                            .SelectMany(x =>
                            {
                                try
                                {
                                    return x.GetTypes();
                                }
                                catch (ReflectionTypeLoadException e)
                                {
                                    return e.Types.Where(t => t != null);
                                }
                            })
                            .Where(t => !t.IsAbstract && defaultAsset.GetType().IsAssignableFrom(t)))
                        .ToArray();

                    var _names = new[] {"Clone"}
                        .Concat(_types.Skip(1).Select(x => x.Name))
                        .ToArray();

                    cloneTypes = s_cloneTypes[defaultType] = new CloneTypes()
                    {
                        types = _types,
                        names = _names,
                    };
                }

                var cloneType = cloneTypes.types[EditorGUI.Popup(cloneButtonRect, 0, cloneTypes.names)];
                if (cloneType != null)
                {
                    var cloneDefault = SettingsAssetBase.GetDefaultAsset(cloneType);
                    CloneDefaultValuesToOverride(property, overrideProperty, cloneDefault);
                }

                var prevEnabled = GUI.enabled;
                GUI.enabled = GUI.enabled && !overrideProperty.hasMultipleDifferentValues &&
                              overrideProperty.objectReferenceValue != null;
                if (GUI.Button(applyButtonRect, "Apply"))
                {
                    Object applyToAsset =
                        SettingsAssetBase.GetDefaultAsset(overrideProperty.objectReferenceValue.GetType());
                    if (defaultAsset.GetType() != overrideProperty.objectReferenceValue.GetType())
                    {
                        var messageStr = string.Format(
                            "You're applying changes to a subclass asset type. Not the base type for this reference" +
                            "\n\nSubclass Type (applying to this!):\n - {0} " +
                            "\n\nBase Type (not this!):\n - {1}",
                            overrideProperty.objectReferenceValue.GetType().Name,
                            defaultAsset.GetType().Name);

                        if (!EditorUtility.DisplayDialog("Hold up!", messageStr, "Proceed", "Cancel"))
                        {
                            applyToAsset = null;
                        }
                    }

                    if (applyToAsset != null)
                    {
                        ApplyOverrideValuesToDefaultAsset(overrideProperty, applyToAsset);
                    }
                }
                GUI.enabled = prevEnabled;		
            }
        }

        private static void CloneDefaultValuesToOverride(SerializedProperty property, SerializedProperty overrideProperty, Object defaultAsset)
        {
            int mode = !overrideProperty.serializedObject.isEditingMultipleObjects
                ? 1 : EditorUtility.DisplayDialogComplex("Cloning Defaults for multiple objects",
                    "You're about to clone the default settings for multiple objects. How do you want to do this?",
                    "Single Shared Copy",
                    "Unique copy for each",
                    "Cancel");

            if (mode < 2)
            {
                Object sharedInstance = null;
                int id = 0;
                for (int i = 0; i < overrideProperty.serializedObject.targetObjects.Length; i++)
                {
                    var target = overrideProperty.serializedObject.targetObjects[i];				
                    if (sharedInstance == null || mode==1)
                    {
                        var path = mode == 1
                            ? GetPath(target)
                            : GetLongestCommonPrefix(overrideProperty.serializedObject.targetObjects.Select(x=>Path.GetDirectoryName(GetPath(x))).ToArray())+"/New Shared";
					 
                        var filename = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path));

                        var propertyPath = property.GetSanitizedPropertyPath();
                        var stripStrings = new[] {"settings", "m_Settings"};
                        var propertySeperator = Array.Exists(stripStrings, x=>string.Equals(propertyPath, x, StringComparison.InvariantCultureIgnoreCase)) 
                            ? ""
                            :"."+propertyPath;

                        path = Path.GetDirectoryName(path)
                               + "/" + filename
                               + propertySeperator
                               + "." + defaultAsset.GetType().Name
                               + ".asset";

                        var outputPath = path;
                        while (File.Exists(outputPath))
                        {
                            ++id;
                            outputPath = Path.GetDirectoryName(path) 
                                         + "/" + filename
                                         + propertySeperator
                                         + "_" + id 
                                         + "." + defaultAsset.GetType().Name						
                                         + ".asset";
                        }

                        AssetDatabase.CopyAsset(AssetDatabase.GetAssetPath(defaultAsset), outputPath);
                        sharedInstance = AssetDatabase.LoadAssetAtPath<Object>(outputPath);            
                        EditorGUIUtility.PingObject(sharedInstance);
                    }

                    if (mode == 0)
                    {
                        overrideProperty.objectReferenceValue = sharedInstance;                                
                        break;
                    }
                    else if (mode == 1)
                    {
                        var so = new SerializedObject(target);
                        var uniqueSoProperty = so.FindProperty(overrideProperty.propertyPath);
                        uniqueSoProperty.objectReferenceValue = sharedInstance;
                        so.ApplyModifiedProperties();
                        overrideProperty.serializedObject.Update();                                
                    }
                }

                AssetDatabase.SaveAssets();
            }
        }

        private static string GetPath(Object target)
        {
            // 1. check for an asset path (ScriptableObject)
            var path = AssetDatabase.GetAssetPath(target);

            // 2. check for a prefab path (could be referenced by a monobehaviour)
            var prefab = PrefabUtility.GetCorrespondingObjectFromSource(target);
            if (prefab !=null && string.IsNullOrEmpty(path))
            {
                path = AssetDatabase.GetAssetPath(prefab);
            }

            // 3. check for a scene path
            var comp = target as Component;
            if (comp != null)
            {
#if UNITY_2018_3_OR_NEWER
                var prefabStage = PrefabStageUtility.GetCurrentPrefabStage();
                if (prefabStage!=null && prefabStage.IsPartOfPrefabContents(comp.gameObject))
                {
                    path = Path.GetDirectoryName(prefabStage.prefabAssetPath)
                           +"/"+Path.GetFileNameWithoutExtension(prefabStage.prefabAssetPath)
                           +"."+comp.gameObject.name+".obj";  // fake extension as we strip it
                }
                else 
#endif
                if (comp.gameObject.scene.IsValid())
                {
                    path = Path.GetDirectoryName(comp.gameObject.scene.path)
                           +"/"+Path.GetFileNameWithoutExtension(comp.gameObject.scene.path)
                           +"."+comp.gameObject.name+".obj";  // fake extension as we strip it
                }

            }

            // 4. give up
            if (string.IsNullOrEmpty(path))
            {
                path = "Assets/" + target.name;
            }

            return path;
        }

        private static string GetLongestCommonPrefix(string[] s)
        {
            int k = s[0].Length;
            for (int i = 1; i < s.Length; i++)
            {
                k = Mathf.Min(k, s[i].Length);
                for (int j = 0; j < k; j++)
                    if (s[i][j] != s[0][j])
                    {
                        k = j;
                        break;
                    }
            }
            return s[0].Substring(0, k);
        }

        private static void ApplyOverrideValuesToDefaultAsset(SerializedProperty overrideProperty, Object defaultAsset)
        {
            var defaultSo = new SerializedObject(defaultAsset);
            var overrideSo = new SerializedObject(overrideProperty.objectReferenceValue);
            var prop = overrideSo.GetIterator();
            while (prop.NextVisible(true))
            {
                if (defaultSo.FindProperty(prop.propertyPath)!=null)
                {
                    defaultSo.CopyFromSerializedProperty(prop);				
                }
            }
            defaultSo.ApplyModifiedProperties();
        }

        private static void OnGUI_DrawPropertyField(Rect position, GUIContent label, Rect propertyFieldRect, SerializedProperty overrideProperty)
        {
            var usingDefault = overrideProperty.objectReferenceValue == null;

            var prevColor = GUI.color;
            GUI.color = usingDefault ? new Color(.65f, 1f, .65f, 1) : Color.cyan;
		
            GUI.Box(EditorGUI.IndentedRect(position), "", "sv_iconselector_selection");
            var content = new GUIContent(label);
            if (overrideProperty.hasMultipleDifferentValues)
            {
                content.text += " (multiple)";
            }
            else if (overrideProperty.objectReferenceValue)
            {
                content.text += string.Format(" ({0})", overrideProperty.objectReferenceValue.GetType().Name);
            }
            else 
            {
                content.text += " (default)";
            }

            GUI.color = prevColor;

            var prevWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = Mathf.Max((int)GUI.skin.label.CalcSize(content).x,EditorGUIUtility.labelWidth);
            EditorGUI.PropertyField(propertyFieldRect, overrideProperty, content);
            EditorGUIUtility.labelWidth = prevWidth;
        }

        private static void OnGUI_DrawOverridePopout(Rect position, SerializedProperty overrideProperty, Object defaultAsset)
        {
            // we only have to draw the popout button if the default asset is being used.
            // The ObjectDrawer will draw it if there's an overriden value object
            if (!overrideProperty.hasMultipleDifferentValues && overrideProperty.objectReferenceValue != null)
            {
                return;
            }

            if (defaultAsset)
            {
                PopupEditorWindow.DrawPopOutButton(position, defaultAsset);
            }
        }
    }
}
#endif