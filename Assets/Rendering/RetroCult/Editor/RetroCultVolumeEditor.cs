using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace BloodPachinko.Rendering.Editor
{
    [CustomEditor(typeof(RetroCultVolume))]
    public sealed class RetroCultVolumeEditor : VolumeComponentEditor
    {
        void Field(string property, string label, string tooltip) =>
            PropertyField(Unpack(serializedObject.FindProperty(property)), new GUIContent(label, tooltip));
        static void Group(string label)
        {
            EditorGUILayout.Space(7);
            EditorGUILayout.LabelField(label, EditorStyles.boldLabel);
        }
        public override void OnInspectorGUI()
        {
            EditorGUILayout.HelpBox("方案 2：粗像素邪典。参数实时生效。虚拟高度越小，像素越粗；抖动频率不影响游戏帧率。整体亮暗、色温、辉光和暗角在下方标准组件中调整。", MessageType.Info);
            Field("strength", "复古纹理总强度", "0 关闭像素、抖动、色阶与颗粒。若要同时关闭调色，请把整个 Volume 的 Weight 设为 0。");
            Field("sceneView", "Scene 窗口也预览", "还需打开 Scene 窗口的后处理显示。Game 窗口始终按实际效果显示。");
            Group("像素轮廓与动态抖动");
            Field("virtualHeight", "虚拟像素高度", "默认 360：1080p 约每 3 像素一个格，4K 约每 6 像素一个格，画面比例一致。改成 540 会更细。");
            Field("pixelInterior", "表面像素感", "控制物体内部纹理的像素化程度。");
            Field("pixelEdges", "边缘像素感", "控制轮廓的阶梯感；0 同时关闭边缘抖动。");
            Field("jitterAmount", "边缘抖动幅度", "以虚拟像素为单位，只改变采样，不移动摄像机、物体或碰撞。0 停止轮廓位移。");
            Field("jitterRate", "抖动更新频率 / 秒", "默认 6。设为 0 冻结复古噪声，不暂停游戏。");
            Field("jitterCellSize", "抖动分组大小", "同一区块同步改变采样位置。默认 8。");
            Field("edgeThreshold", "边缘识别门槛", "越低越多纹理细节参与抖动。");
            Field("edgeSoftness", "边缘过渡宽度", "控制效果从平坦表面到轮廓的过渡。");
            Group("色调分离与点阵");
            Field("posterization", "色阶分离强度", "降低后可缓和墙面和金属上的色块。");
            Field("colorSteps", "颜色阶数", "默认 12；数值越低，颜色断层越明显。");
            Field("luminanceSteps", "明暗阶数", "默认 8；数值越低，光照层级越硬。");
            Field("dither", "点阵抖色", "在色阶交界处加入有序的像素点阵，不是镜头震动。");
            Group("颗粒与氛围");
            Field("grain", "动态颗粒", "默认 0.03，避免把材质噪点盖住。");
            Field("flicker", "轻微亮度起伏", "默认 0.01，相当于很轻的亮度波动；0 关闭。");
            Field("scanlines", "扫描线（可选）", "方案 2 默认关闭，可手动少量加入。");
            Group("UI 可读性");
            Field("protectText", "保护文字清晰度", "自动跟随实体 UI 文字，减少其像素化、抖动和颗粒；屏幕叠加 UI 本身不经过此后处理。");
            Field("textPadding", "文字保护边距", "以 1080p 像素为基准，自动适配 4K。");
            EditorGUILayout.Space(8);
            if (GUILayout.Button("恢复方案 2 的复古纹理参数"))
            {
                Undo.RecordObject(target, "恢复粗像素邪典参数");
                ((RetroCultVolume)target).ApplySchemeTwo();
                EditorUtility.SetDirty(target);
                serializedObject.Update();
            }
        }
    }
}
