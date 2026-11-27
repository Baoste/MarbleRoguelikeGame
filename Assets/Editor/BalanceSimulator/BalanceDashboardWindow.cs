using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace MarblesECS.Editor
{
    public sealed class BalanceDashboardWindow : EditorWindow
    {
        private int physicalSamples = 1800;
        private int seed = 20260930;
        private string status;

        [MenuItem("Tools/Pachinko/Balance Dashboard")]
        public static void Open() => GetWindow<BalanceDashboardWindow>("Balance Dashboard");

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Physical Sampling", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Samples the authored TestScene using the gameplay PhysX bridge.", MessageType.Info);
            seed = EditorGUILayout.IntField("Seed", seed);
            physicalSamples = Mathf.Max(90, EditorGUILayout.IntField("Samples", physicalSamples));
            if (GUILayout.Button("Run Physical Sampling")) RunPhysical();
            if (GUILayout.Button("Open Results Folder"))
                EditorUtility.RevealInFinder(Path.GetDirectoryName(Path.GetFullPath(BalancePhysicalSampler.DefaultCsv)));
            if (!string.IsNullOrEmpty(status)) EditorGUILayout.LabelField(status, EditorStyles.wordWrappedLabel);
        }

        private void RunPhysical()
        {
            try
            {
                BalanceBatch.RunPhysicalSampling(physicalSamples, seed);
                status = "Physical profile updated.";
            }
            catch (Exception error)
            {
                status = error.Message;
                Debug.LogException(error);
            }
        }
    }
}
