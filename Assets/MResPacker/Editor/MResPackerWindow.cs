using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MResPacker
{
    public sealed class MResPackerWindow : EditorWindow
    {
        [SerializeField] private MResBuildProfile profile;
        private MResAnalysisReport report;
        private Vector2 scroll;
        private string buildResult;
        [MenuItem("Tools/MRes/Packer")]
        public static void Open() => GetWindow<MResPackerWindow>("MRes Packer");
        private void OnEnable()
        {
            minSize = new Vector2(430, 560);
            if (profile == null) profile = AssetDatabase.LoadAssetAtPath<MResBuildProfile>("Assets/MResPacker/Authoring/Profiles/BouncePad.asset");
        }
        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorGUILayout.LabelField("MRes v1 · Prefab + Lua → 单文件资源", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            profile = (MResBuildProfile)EditorGUILayout.ObjectField("Build Profile", profile, typeof(MResBuildProfile), false);
            if (EditorGUI.EndChangeCheck()) { report = null; buildResult = null; }
            if (GUILayout.Button("新建打包配置")) CreateProfile();
            if (profile == null)
            {
                EditorGUILayout.HelpBox("选择或新建配置；也可以创建完整示例开始制作。", MessageType.Info);
                if (GUILayout.Button("创建 / 定位完整示例")) profile = MResExampleCreator.Create();
                EditorGUILayout.EndScrollView(); return;
            }
            var serialized = new SerializedObject(profile); serialized.Update();
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(serialized.FindProperty("SourcePrefab"), new GUIContent("Source Prefab"));
            EditorGUILayout.PropertyField(serialized.FindProperty("LuaScript"), new GUIContent("Lua Script (.lua)"));
            EditorGUILayout.PropertyField(serialized.FindProperty("ShaderRegistry"), new GUIContent("Shader ID Registry"));
            var metadata = serialized.FindProperty("Metadata");
            EditorGUILayout.LabelField("Metadata", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(metadata.FindPropertyRelative("name"), new GUIContent("Name"));
            EditorGUILayout.PropertyField(metadata.FindPropertyRelative("description"), new GUIContent("Description"));
            EditorGUILayout.PropertyField(metadata.FindPropertyRelative("value"), new GUIContent("Value"));
            var type = metadata.FindPropertyRelative("type");
            int index = type.stringValue == "medicine" ? 0 : type.stringValue == "device" ? 1 : -1;
            int next = EditorGUILayout.Popup("Type", index, new[] { "Medicine", "Device" });
            if (next != index) type.stringValue = next == 0 ? "medicine" : "device";
            EditorGUILayout.Space();
            EditorGUILayout.PropertyField(serialized.FindProperty("ExtraFiles"), new GUIContent("附加业务文件（可选）"), true);
            EditorGUILayout.PropertyField(serialized.FindProperty("OutputPath"), new GUIContent("Output Path"));
            if (EditorGUI.EndChangeCheck()) { serialized.ApplyModifiedProperties(); report = null; buildResult = null; }
            if (GUILayout.Button("选择输出文件…"))
            {
                string current = string.IsNullOrWhiteSpace(profile.OutputPath) ? "Assets/MResPacker/Output/Package.mres" : profile.OutputPath;
                string selected = EditorUtility.SaveFilePanel("Build MRes", Path.GetDirectoryName(Path.GetFullPath(current)), Path.GetFileNameWithoutExtension(current), "mres");
                if (!string.IsNullOrEmpty(selected))
                {
                    Undo.RecordObject(profile, "Set MRes output");
                    string project = Path.GetFullPath(".").Replace('\\', '/') + "/";
                    profile.OutputPath = selected.StartsWith(project, StringComparison.OrdinalIgnoreCase) ? selected.Substring(project.Length) : selected;
                    EditorUtility.SetDirty(profile);
                }
            }
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Analyze", GUILayout.Height(30))) { AssetDatabase.SaveAssets(); report = MResBuildPipeline.Analyze(profile); buildResult = null; }
            if (GUILayout.Button("Build MRes", GUILayout.Height(30)))
            {
                try { AssetDatabase.SaveAssets(); report = MResBuildPipeline.Build(profile); buildResult = "已生成：" + Path.GetFullPath(profile.OutputPath); }
                catch (Exception e) { buildResult = null; report = MResBuildPipeline.Analyze(profile); if (report.CanBuild) report.Errors.Add(e.Message); }
            }
            EditorGUILayout.EndHorizontal();
            if (buildResult != null) EditorGUILayout.HelpBox(buildResult, MessageType.Info);
            if (report != null) DrawReport();
            EditorGUILayout.EndScrollView();
        }
        private void DrawReport()
        {
            EditorGUILayout.Space(); EditorGUILayout.LabelField("Analysis", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Node Count", report.NodeCount.ToString());
            EditorGUILayout.LabelField("Mesh Count", report.MeshCount.ToString());
            EditorGUILayout.LabelField("Material Count", report.MaterialCount.ToString());
            EditorGUILayout.LabelField("Texture Count", report.TextureCount.ToString());
            EditorGUILayout.LabelField("PhysicsMaterial Count", report.PhysicsMaterialCount.ToString());
            EditorGUILayout.LabelField("Collider Count", report.ColliderCount.ToString());
            EditorGUILayout.LabelField("Rigidbody Count", report.RigidbodyCount.ToString());
            EditorGUILayout.LabelField("Lua", report.HasLua ? "UTF-8 · scripts/main.lua" : "None");
            EditorGUILayout.LabelField("Estimated Size", EditorUtility.FormatBytes(report.EstimatedSize));
            EditorGUILayout.LabelField("Shader IDs", string.Join(", ", report.ShaderIds));
            EditorGUILayout.LabelField("Metadata", report.Metadata ?? "", EditorStyles.wordWrappedLabel);
            EditorGUILayout.LabelField("Unsupported Components", report.UnsupportedComponents.Count.ToString());
            foreach (string error in report.UnsupportedComponents) EditorGUILayout.HelpBox(error, MessageType.Error);
            foreach (string error in report.Errors) EditorGUILayout.HelpBox(error, MessageType.Error);
            if (report.CanBuild) EditorGUILayout.HelpBox("分析通过，可以构建。构建时会重新读取源资源并校验。", MessageType.Info);
        }
        private void CreateProfile()
        {
            string path = EditorUtility.SaveFilePanelInProject("New MRes Profile", "NewDevice", "asset", "保存打包配置", "Assets/MResPacker");
            if (string.IsNullOrEmpty(path)) return;
            profile = CreateInstance<MResBuildProfile>(); AssetDatabase.CreateAsset(profile, path); AssetDatabase.SaveAssets();
            Selection.activeObject = profile; report = null;
        }
    }
}
