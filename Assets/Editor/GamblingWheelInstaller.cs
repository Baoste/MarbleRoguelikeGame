using System;
using System.Collections.Generic;
using System.Linq;
using MarblesECS;
using MarblesECS.PhysX;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class GamblingWheelInstaller
{
    private const string Art = "Assets/Art/UI/GamblingWheel";
    private const string PrefabPath = "Assets/Prefabs/UI/GamblingWheel.prefab";

    [MenuItem("Marbles ECS/UI/配置左上角赌博转盘")]
    public static void ConfigureCurrentScene()
    {
        if (EditorApplication.isPlaying) throw new InvalidOperationException("请先退出运行模式。");
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        var roots = scene.GetRootGameObjects();
        var all = roots.SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
        var game = roots.SelectMany(g => g.GetComponentsInChildren<MarbleGameController>(true)).Single();
        var existing = roots.SelectMany(g => g.GetComponentsInChildren<GamblingWheelDisplay>(true)).SingleOrDefault();
        if (existing)
        {
            existing.Controller = game;
            StretchFill(existing.TimerBar.fillRect);
            EditorSceneManager.MarkSceneDirty(scene);
            return;
        }

        // These are the old presentation objects only. The real score/drain zones remain in place.
        foreach (var t in all.Where(t => t && (t.name == "待结算提示" ||
            t.name == "RandomScore" && t.GetComponent<TMP_Text>())).ToArray())
            Undo.DestroyObjectImmediate(t.gameObject);
        foreach (var label in all.Where(t => t).Select(t => t.GetComponent<TMP_Text>())
            .Where(t => t && t.text == "Pending Score:").ToArray())
            Undo.DestroyObjectImmediate(label.transform.parent.gameObject);
        var uiController = all.Single(t => t && t.name == "UIController");
        var obsolete = uiController.GetComponents<MonoBehaviour>().FirstOrDefault(c => c && c.GetType().Name == "RandomScoreResultUI");
        if (obsolete) Undo.DestroyObjectImmediate(obsolete);
        else if (GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(uiController.gameObject) == 1)
            GameObjectUtility.RemoveMonoBehavioursWithMissingScript(uiController.gameObject);

        Folder(Art); Folder("Assets/Prefabs/UI");
        var font = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>("Assets/Art/Fonts/ShopChineseSDF.asset");
        if (!font) throw new InvalidOperationException("中文字体缺失。");
        var shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (!shader) shader = Shader.Find("Unlit/Color");
        var rim = MaterialAsset("Rim", new Color(.72f, .49f, .22f), shader);
        var dark = MaterialAsset("Frame", new Color(.06f, .07f, .07f), shader);
        GameObject template = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (!template)
        {
            var model = new GameObject("GamblingWheelModel");
            var rotor = new GameObject("Rotor").transform; rotor.SetParent(model.transform, false);
            Disc(rotor, "OuterRim", 0, 360, .96f, 1.12f, .12f, rim);
            Disc(rotor, "Frame", 0, 360, 0, 1.04f, .1f, dark);
            for (int i = 0; i < GamblingWheelRules.OutcomeCount; i++)
            {
                var material = MaterialAsset("Sector" + i, GamblingWheelDisplay.SectorColor(i), shader);
                var sector = Disc(rotor, "Sector" + i, 90 - i * 45 - 21.5f, 43, .32f, .99f, .14f, material);
                float angle = (90 - i * 45) * Mathf.Deg2Rad;
                var label = new GameObject("Multiplier", typeof(TextMeshPro)).GetComponent<TextMeshPro>();
                label.transform.SetParent(sector.transform, false);
                label.transform.localPosition = new Vector3(Mathf.Cos(angle) * .72f, Mathf.Sin(angle) * .72f, -.151f);
                label.rectTransform.sizeDelta = new Vector2(.5f, .3f);
                label.text = GamblingWheelRules.Multiplier(i).ToString(); label.font = font;
                label.fontSize = 4.8f; label.alignment = TextAlignmentOptions.Center;
                label.fontStyle = FontStyles.Bold; label.color = new Color(1, .93f, .77f);
                label.enableWordWrapping = false;
            }
            Disc(model.transform, "Hub", 0, 360, 0, .29f, .19f, rim);
            foreach (var t in model.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 31;
            template = PrefabUtility.SaveAsPrefabAsset(model, PrefabPath);
            UnityEngine.Object.DestroyImmediate(model);
        }
        var modelInstance = (GameObject)PrefabUtility.InstantiatePrefab(template, scene);
        modelInstance.transform.position = new Vector3(0, 1000, 0);
        Undo.RegisterCreatedObjectUndo(modelInstance, "创建转盘模型");
        var cameraObject = new GameObject("GamblingWheelCamera", typeof(Camera));
        Undo.RegisterCreatedObjectUndo(cameraObject, "创建转盘相机");
        cameraObject.transform.SetParent(modelInstance.transform, false);
        cameraObject.transform.localPosition = new Vector3(0, .8f, -5);
        cameraObject.transform.LookAt(modelInstance.transform);
        var modelCamera = cameraObject.GetComponent<Camera>();
        modelCamera.fieldOfView = 28; modelCamera.nearClipPlane = .1f; modelCamera.farClipPlane = 10;
        modelCamera.clearFlags = CameraClearFlags.SolidColor; modelCamera.backgroundColor = new Color(.06f,.07f,.07f,0);
        modelCamera.cullingMask = 1 << 31; modelCamera.depth = -20; modelCamera.enabled = false;
        modelCamera.allowHDR = false; modelCamera.useOcclusionCulling = false;

        var canvasObject = new GameObject("GamblingWheelCanvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler));
        Undo.RegisterCreatedObjectUndo(canvasObject, "创建转盘展示");
        var canvas = canvasObject.GetComponent<Canvas>(); canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 49;
        var scaler = canvasObject.GetComponent<UnityEngine.UI.CanvasScaler>();
        scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080); scaler.matchWidthOrHeight = .5f;
        var panel = Rect(canvas.transform, "WheelPanel", 28, -28, 350, 444);
        var image = panel.gameObject.AddComponent<UnityEngine.UI.Image>(); image.color = new Color(.07f,.08f,.075f,.94f); image.raycastTarget = false;
        var group = panel.gameObject.AddComponent<CanvasGroup>(); group.interactable = group.blocksRaycasts = false;
        Label(panel, font, "Title", "赌博转盘", 18, -12, 314, 32, 24);
        var modelRect = Rect(panel, "Model", 37, -48, 276, 276);
        var raw = modelRect.gameObject.AddComponent<UnityEngine.UI.RawImage>(); raw.raycastTarget = false;
        var view = canvasObject.AddComponent<GamblingWheelDisplay>();
        view.Controller = game; view.DisplayGroup = group; view.ModelImage = raw; view.ModelCamera = modelCamera;
        view.Rotor = modelInstance.transform.Find("Rotor");
        view.Sectors = Enumerable.Range(0, 8).Select(i => view.Rotor.Find("Sector" + i).GetComponent<Renderer>()).ToArray();
        view.PoolText = Label(panel, font, "Pool", "奖池  0  ·  0 球", 18, -322, 314, 30, 21);
        view.TimerText = Label(panel, font, "Timer", "等待进球", 18, -356, 314, 26, 20);
        view.ResultText = Label(panel, font, "Result", "−2 / −1 / 0 / 1 / 5 / 10 / 20 / 50", 18, -402, 314, 28, 19);
        var bar = Rect(panel, "Countdown", 18, -388, 314, 6);
        var bg = bar.gameObject.AddComponent<UnityEngine.UI.Image>(); bg.color = new Color(.19f,.21f,.19f); bg.raycastTarget = false;
        var fill = Rect(bar, "Fill", 0, 0, 314, 6);
        StretchFill(fill);
        var fillImage = fill.gameObject.AddComponent<UnityEngine.UI.Image>(); fillImage.color = new Color(.88f,.65f,.29f); fillImage.raycastTarget = false;
        var slider = bar.gameObject.AddComponent<UnityEngine.UI.Slider>(); slider.fillRect = fill;
        slider.minValue = 0; slider.maxValue = 1; slider.interactable = false;
        slider.transition = UnityEngine.UI.Selectable.Transition.None;
        slider.navigation = new UnityEngine.UI.Navigation { mode = UnityEngine.UI.Navigation.Mode.None };
        slider.SetValueWithoutNotify(0); view.TimerBar = slider;
        foreach (var t in canvasObject.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = 5;
        EditorUtility.SetDirty(view);
        EditorSceneManager.MarkSceneDirty(scene);
        AssetDatabase.SaveAssets();
    }

    private static void Folder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\','/'); Folder(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(path));
    }
    private static Material MaterialAsset(string name, Color color, Shader shader)
    {
        string path = Art + "/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material) return material;
        material = new Material(shader) { name = name, color = color };
        AssetDatabase.CreateAsset(material, path); return material;
    }
    private static GameObject Disc(Transform parent, string name, float start, float arc, float inner, float outer, float depth, Material material)
    {
        string path = Art + "/" + name + ".asset";
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (!mesh)
        {
            var vertices = new List<Vector3>(); var triangles = new List<int>();
            int steps = Math.Max(8, Mathf.CeilToInt(arc / 5));
            for (int i = 0; i <= steps; i++)
            {
                float angle = (start + arc * i / steps) * Mathf.Deg2Rad;
                var direction = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0);
                vertices.Add(direction * inner + Vector3.back * depth);
                vertices.Add(direction * outer + Vector3.back * depth);
                vertices.Add(direction * outer + Vector3.forward * depth);
                vertices.Add(direction * inner + Vector3.forward * depth);
                if (i == 0) continue;
                int a = (i - 1) * 4, b = i * 4;
                Quad(triangles, a, b, b+1, a+1);
                Quad(triangles, a+1, b+1, b+2, a+2);
                Quad(triangles, a+2, b+2, b+3, a+3);
                Quad(triangles, a+3, b+3, b, a);
            }
            Quad(triangles,0,1,2,3); int end=steps*4; Quad(triangles,end+3,end+2,end+1,end);
            mesh = new Mesh { name = name }; mesh.SetVertices(vertices); mesh.SetTriangles(triangles,0);
            mesh.RecalculateNormals(); mesh.RecalculateBounds(); AssetDatabase.CreateAsset(mesh,path);
        }
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); go.transform.SetParent(parent,false);
        go.GetComponent<MeshFilter>().sharedMesh=mesh; go.GetComponent<MeshRenderer>().sharedMaterial=material;
        return go;
    }
    private static void Quad(List<int> t, int a, int b, int c, int d)
    { t.Add(a);t.Add(b);t.Add(c);t.Add(a);t.Add(c);t.Add(d); }
    private static RectTransform Rect(Transform parent, string name, float x, float y, float w, float h)
    {
        var r = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>(); r.SetParent(parent,false);
        r.anchorMin=r.anchorMax=r.pivot=new Vector2(0,1); r.anchoredPosition=new Vector2(x,y);r.sizeDelta=new Vector2(w,h);return r;
    }
    private static void StretchFill(RectTransform fill)
    {
        fill.anchorMin = Vector2.zero; fill.anchorMax = Vector2.one;
        fill.offsetMin = fill.offsetMax = Vector2.zero;
    }
    private static TMP_Text Label(Transform parent, TMP_FontAsset font, string name, string text, float x,float y,float w,float h,float size)
    {
        var label=Rect(parent,name,x,y,w,h).gameObject.AddComponent<TextMeshProUGUI>();label.font=font;label.text=text;
        label.fontSize=size;label.enableAutoSizing=true;label.fontSizeMin=16;label.fontSizeMax=size;
        label.alignment=TextAlignmentOptions.Center;label.enableWordWrapping=false;label.raycastTarget=false;
        label.color=new Color(.95f,.89f,.74f);return label;
    }
}
