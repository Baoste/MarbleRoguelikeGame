using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using MarblesECS.PhysX;

namespace MarblesECS.Editor
{
    internal sealed class BoardLayoutEditorWindow : EditorWindow
    {
        private enum Tool { Select, Pin, ScoreZone, RandomZone, CollisionPin }

        private const string LegacyAssetFolder = "Assets/Config/Resources/BoardLayouts/";
        private static readonly string[] Tools = { "Select", "Pin", "Score Zone", "Random Zone", "Collision Pin" };
        private static readonly string[] ZoneTypes = { nameof(MarbleContactKind.Score), nameof(MarbleContactKind.RandomScore) };
        private static readonly Color PinColor = new Color(.96f, .69f, .29f);
        private static readonly Color CollisionPinColor = new Color(.52f, .76f, .86f);
        private static readonly Color ScoreColor = new Color(.24f, .74f, .47f);
        private static readonly Color RandomColor = new Color(.67f, .45f, .94f);

        [SerializeField] private List<BoardLayoutPin> pins = new List<BoardLayoutPin>();
        [SerializeField] private List<BoardLayoutZone> zones = new List<BoardLayoutZone>();
        [SerializeField] private BoardPlacementSurface surface;
        [SerializeField] private string assetPath;
        [SerializeField] private Tool tool;
        [SerializeField] private int pinIndex = -1, zoneIndex = -1;
        private Vector2 inspectorScroll;
        private Vector2 dragOffset;
        private string status;
        private MessageType statusType;

        [MenuItem("Marbles ECS/Board Layout Editor")]
        private static void OpenWindow() => GetWindow<BoardLayoutEditorWindow>().Show();

        private void OnEnable()
        {
            titleContent = new GUIContent("Board Layout");
            minSize = new Vector2(660, 420);
            saveChangesMessage = "Save your changes to this board layout?";
            if (assetPath?.StartsWith(LegacyAssetFolder, StringComparison.OrdinalIgnoreCase) == true)
                assetPath = BoardLayoutLoader.AssetFolder + "/" + assetPath.Substring(LegacyAssetFolder.Length);
            if (!surface) surface = FindObjectOfType<BoardPlacementSurface>();
        }

        private void OnGUI()
        {
            int canvasControl = GUIUtility.GetControlID(FocusType.Passive);
            DrawFileToolbar();
            surface = (BoardPlacementSurface)EditorGUILayout.ObjectField("Board Surface", surface, typeof(BoardPlacementSurface), true);
            BoardLayoutProjection projection;
            try { projection = Projection; }
            catch (ArgumentException error)
            {
                EditorGUILayout.HelpBox(error.Message, MessageType.Error);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                tool = (Tool)GUILayout.Toolbar((int)tool, Tools);
                GUILayout.FlexibleSpace();
                GUILayout.Label($"{pins.Count} pins   {zones.Count} zones", EditorStyles.miniLabel);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                Rect canvas = GUILayoutUtility.GetRect(200, 250, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                DrawInspector();
                DrawCanvas(canvas, projection, canvasControl);
            }
            if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, statusType);
        }

        private BoardLayoutProjection Projection => surface ? surface.LayoutProjection : BoardPlacementSurface.DefaultLayoutProjection;

        private void DrawFileToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                if (GUILayout.Button("New", EditorStyles.toolbarButton) && CanReplaceLayout())
                {
                    pins.Clear();
                    zones.Clear();
                    ResetEditor(null);
                }
                if (GUILayout.Button("Open", EditorStyles.toolbarButton)) OpenLayout();
                if (GUILayout.Button("Save", EditorStyles.toolbarButton)) SaveLayout(false);
                if (GUILayout.Button("Save As", EditorStyles.toolbarButton)) SaveLayout(true);
                GUILayout.FlexibleSpace();
                GUILayout.Label(string.IsNullOrEmpty(assetPath) ? "Untitled" : RelativeFile(assetPath), EditorStyles.miniLabel);
            }
        }

        private void DrawInspector()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox, GUILayout.Width(244), GUILayout.ExpandHeight(true)))
            {
                inspectorScroll = EditorGUILayout.BeginScrollView(inspectorScroll);
                float previousLabelWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 128;
                if (pinIndex >= 0) DrawPinInspector();
                else if (zoneIndex >= 0) DrawZoneInspector();
                else EditorGUILayout.LabelField("Select an object to edit its properties.", EditorStyles.wordWrappedMiniLabel);
                EditorGUIUtility.labelWidth = previousLabelWidth;
                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawPinInspector()
        {
            var pin = pins[pinIndex];
            EditorGUILayout.LabelField(pin.collisionOnly ? "Collision Pin" : "Pin", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            pin.x = EditorGUILayout.FloatField("X", pin.x);
            pin.y = EditorGUILayout.FloatField("Y", pin.y);
            pin.radius = EditorGUILayout.FloatField("Radius", pin.radius);
            if (!pin.collisionOnly)
            {
                pin.scoreMultiplier = EditorGUILayout.FloatField("Score Multiplier", pin.scoreMultiplier);
                pin.rushChanceAdd = EditorGUILayout.FloatField("Rush Chance Add", pin.rushChanceAdd);
            }
            if (EditorGUI.EndChangeCheck()) { pins[pinIndex] = pin; MarkEdited(); }
            if (GUILayout.Button("Delete Pin")) DeleteSelection();
        }

        private void DrawZoneInspector()
        {
            var zone = zones[zoneIndex];
            EditorGUILayout.LabelField("Zone", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            zone.type = ZoneTypes[EditorGUILayout.Popup("Type", zone.Kind == MarbleContactKind.Score ? 0 : 1, ZoneTypes)];
            zone.x = EditorGUILayout.FloatField("X", zone.x);
            zone.y = EditorGUILayout.FloatField("Y", zone.y);
            zone.width = EditorGUILayout.FloatField("Width", zone.width);
            zone.depth = EditorGUILayout.FloatField("Depth", zone.depth);
            zone.triggerHeight = EditorGUILayout.FloatField("Trigger Height", zone.triggerHeight);
            zone.multiplier = EditorGUILayout.FloatField("Multiplier", zone.multiplier);
            zone.flatScore = EditorGUILayout.LongField("Flat Score", zone.flatScore);
            zone.priority = EditorGUILayout.IntField("Priority", zone.priority);
            zone.rushEnabled = EditorGUILayout.Toggle("Rush Enabled", zone.rushEnabled);
            zone.scoreStackEnabled = EditorGUILayout.Toggle("Score Stack Enabled", zone.scoreStackEnabled);
            if (EditorGUI.EndChangeCheck()) { zones[zoneIndex] = zone; MarkEdited(); }
            if (GUILayout.Button("Delete Zone")) DeleteSelection();
        }

        private void DrawCanvas(Rect canvas, BoardLayoutProjection projection, int control)
        {
            if (canvas.width < 80 || canvas.height < 80) return;
            float zoom = Mathf.Min((canvas.width - 54) / projection.MaxWidth, (canvas.height - 55) / projection.Length);
            var origin = new Vector2(canvas.center.x, canvas.y + 29);
            Vector2 Screen(float x, float y) => origin + zoom * projection.ToPlane(x, y);

            var current = Event.current;
            if (current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(canvas, new Color(.12f, .15f, .19f));
                GUI.BeginClip(canvas);
                var offset = canvas.position;
                Handles.BeginGUI();
                var previousColor = Handles.color;
                Vector3 topLeft = Screen(0, 0) - offset, topRight = Screen(1, 0) - offset;
                Vector3 bottomLeft = Screen(0, 1) - offset, bottomRight = Screen(1, 1) - offset;
                Handles.color = new Color(.17f, .21f, .26f);
                Handles.DrawAAConvexPolygon(topLeft, topRight, bottomRight, bottomLeft);
                Handles.color = new Color(.31f, .37f, .45f);
                for (int i = 1; i < 10; i++)
                {
                    float t = i / 10f;
                    Handles.DrawLine(Screen(t, 0) - offset, Screen(t, 1) - offset);
                    Handles.DrawLine(Screen(0, t) - offset, Screen(1, t) - offset);
                }
                Handles.color = new Color(.65f, .71f, .79f);
                Handles.DrawAAPolyLine(2, topLeft, topRight, bottomRight, bottomLeft, topLeft);

                for (int i = 0; i < zones.Count; i++)
                {
                    var zone = zones[i];
                    var center = Screen(zone.x, zone.y) - offset;
                    if (!Drawable(center, zone.width, zone.depth)) continue;
                    var rect = CenteredRect(center, zone.width * zoom, zone.depth * zoom);
                    var color = zone.Kind == MarbleContactKind.Score ? ScoreColor : RandomColor;
                    var fill = color;
                    fill.a = .42f;
                    Handles.DrawSolidRectangleWithOutline(rect, fill, i == zoneIndex ? Color.white : color);
                }
                for (int i = 0; i < pins.Count; i++)
                {
                    var pin = pins[i];
                    var center = Screen(pin.x, pin.y) - offset;
                    if (!Drawable(center, pin.radius, pin.radius)) continue;
                    Handles.color = pin.collisionOnly ? CollisionPinColor : PinColor;
                    Handles.DrawSolidDisc(center, Vector3.forward, pin.radius * zoom);
                    if (i != pinIndex) continue;
                    Handles.color = Color.white;
                    Handles.DrawWireDisc(center, Vector3.forward, pin.radius * zoom + 2);
                }
                Handles.color = previousColor;
                Handles.EndGUI();
                GUI.Label(new Rect(topLeft.x, topLeft.y - 23, 160, 20), "(0, 0)   X →", EditorStyles.whiteMiniLabel);
                GUI.Label(new Rect(bottomLeft.x, bottomLeft.y + 3, 75, 20), "Y ↓", EditorStyles.whiteMiniLabel);
                GUI.EndClip();
            }

            HandleCanvasInput(canvas, projection, zoom, origin, control);
        }

        private void HandleCanvasInput(Rect canvas, BoardLayoutProjection projection, float zoom, Vector2 origin, int control)
        {
            var current = Event.current;
            var point = (current.mousePosition - origin) / zoom;
            var position = projection.ToNormalized(point);
            switch (current.GetTypeForControl(control))
            {
                case EventType.MouseDown when current.button == 0 && canvas.Contains(current.mousePosition):
                    GUI.FocusControl(null);
                    if (tool == Tool.Select)
                    {
                        if (SelectAt(point, projection, zoom)) GUIUtility.hotControl = control;
                    }
                    else if (position.x >= 0 && position.x <= 1 && position.y >= 0 && position.y <= 1)
                    {
                        AddAt(point, projection);
                        tool = Tool.Select;
                    }
                    Repaint();
                    current.Use();
                    break;
                case EventType.MouseDrag when GUIUtility.hotControl == control:
                    MoveSelection(point - dragOffset, projection);
                    current.Use();
                    break;
                case EventType.MouseUp when GUIUtility.hotControl == control:
                    GUIUtility.hotControl = 0;
                    current.Use();
                    break;
            }
        }

        private bool SelectAt(Vector2 point, BoardLayoutProjection projection, float zoom)
        {
            pinIndex = zoneIndex = -1;
            for (int i = pins.Count - 1; i >= 0; i--)
            {
                var pin = pins[i];
                var offset = point - projection.ToPlane(pin.x, pin.y);
                if (!(offset.magnitude <= Mathf.Max(pin.radius, 5 / zoom))) continue;
                pinIndex = i;
                dragOffset = offset;
                return true;
            }
            for (int i = zones.Count - 1; i >= 0; i--)
            {
                var zone = zones[i];
                var offset = point - projection.ToPlane(zone.x, zone.y);
                if (!CenteredRect(Vector2.zero, zone.width, zone.depth).Contains(offset)) continue;
                zoneIndex = i;
                dragOffset = offset;
                return true;
            }
            return false;
        }

        private void AddAt(Vector2 point, BoardLayoutProjection projection)
        {
            pinIndex = zoneIndex = -1;
            float minimumWidth = Mathf.Min(projection.TopWidth, projection.BottomWidth);
            if (tool == Tool.Pin || tool == Tool.CollisionPin)
            {
                pinIndex = pins.Count;
                float radius = Mathf.Min(.1f, Mathf.Min(minimumWidth, projection.Length) * .1f);
                bool collisionOnly = tool == Tool.CollisionPin;
                pins.Add(new BoardLayoutPin { radius = radius, collisionOnly = collisionOnly, scoreMultiplier = collisionOnly ? 1 : 2 });
            }
            else
            {
                zoneIndex = zones.Count;
                zones.Add(new BoardLayoutZone
                {
                    type = tool == Tool.ScoreZone ? ZoneTypes[0] : ZoneTypes[1],
                    width = Mathf.Min(1.25f, minimumWidth * .3f), depth = Mathf.Min(.55f, projection.Length * .1f),
                    triggerHeight = 2.2f, multiplier = 1, rushEnabled = true, scoreStackEnabled = true
                });
            }
            MoveSelection(point, projection);
        }

        private void MoveSelection(Vector2 point, BoardLayoutProjection projection)
        {
            if (pinIndex >= 0)
            {
                var pin = pins[pinIndex];
                var position = Fit(point, projection, pin.radius, pin.radius);
                pin.x = position.x;
                pin.y = position.y;
                pins[pinIndex] = pin;
            }
            else if (zoneIndex >= 0)
            {
                var zone = zones[zoneIndex];
                var position = Fit(point, projection, zone.width * .5f, zone.depth * .5f);
                zone.x = position.x;
                zone.y = position.y;
                zones[zoneIndex] = zone;
            }
            MarkEdited();
        }

        private static Vector2 Fit(Vector2 point, BoardLayoutProjection projection, float halfWidth, float halfDepth)
        {
            halfDepth = Mathf.Clamp(halfDepth, 0, projection.Length * .5f);
            point.y = Mathf.Clamp(point.y, halfDepth, projection.Length - halfDepth);
            float first = projection.WidthAt((point.y - halfDepth) / projection.Length);
            float last = projection.WidthAt((point.y + halfDepth) / projection.Length);
            float limit = Mathf.Max(0, Mathf.Min(first, last) * .5f - Mathf.Max(0, halfWidth));
            point.x = Mathf.Clamp(point.x, -limit, limit);
            return projection.ToNormalized(point);
        }

        private void DeleteSelection()
        {
            if (pinIndex >= 0) pins.RemoveAt(pinIndex);
            else zones.RemoveAt(zoneIndex);
            pinIndex = zoneIndex = -1;
            MarkEdited();
        }

        private void MarkEdited()
        {
            hasUnsavedChanges = true;
            status = null;
            Repaint();
        }

        private void ResetEditor(string file)
        {
            assetPath = file;
            pinIndex = zoneIndex = -1;
            tool = Tool.Select;
            status = null;
            hasUnsavedChanges = false;
        }

        private bool CanReplaceLayout() => !hasUnsavedChanges || EditorUtility.DisplayDialog(
            "Board Layout", "Discard the current unsaved changes?", "Discard Changes", "Cancel");

        private void OpenLayout()
        {
            string file = EditorUtility.OpenFilePanel("Open Board Layout", Path.GetFullPath(BoardLayoutLoader.AssetFolder), "json");
            if (string.IsNullOrEmpty(file)) return;
            string path = FileUtil.GetProjectRelativePath(file);
            try
            {
                string relative = RelativeFile(path);
                var layout = BoardLayoutLoader.Parse(File.ReadAllText(path), relative);
                if (!CanReplaceLayout()) return;
                pins = new List<BoardLayoutPin>(layout.Pins);
                zones = new List<BoardLayoutZone>(layout.Zones);
                ResetEditor(path);
            }
            catch (Exception error) when (error is ArgumentException || error is IOException || error is UnauthorizedAccessException)
            { ShowError(error); }
        }

        public override void SaveChanges()
        {
            if (!SaveLayout(false)) throw new InvalidOperationException(status ?? "Board layout save was canceled.");
        }

        private bool SaveLayout(bool saveAs)
        {
            string path = assetPath;
            if (saveAs || string.IsNullOrEmpty(path))
            {
                path = EditorUtility.SaveFilePanelInProject("Save Board Layout", Path.GetFileNameWithoutExtension(assetPath) ?? "NewBoard",
                    "json", $"Save under {BoardLayoutLoader.AssetFolder}.", BoardLayoutLoader.AssetFolder);
                if (string.IsNullOrEmpty(path)) return false;
            }
            try
            {
                string file = RelativeFile(path);
                string json = BoardLayoutLoader.ToJson(pins, zones);
                Projection.Validate(BoardLayoutLoader.Parse(json, file));
                WriteLayoutFile(path, json);
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                assetPath = path;
                status = $"Saved {file}. Set the Day's boardLayout to \"{file}\".";
                statusType = MessageType.Info;
                base.SaveChanges();
                return true;
            }
            catch (Exception error) when (error is ArgumentException || error is IOException || error is UnauthorizedAccessException)
            { ShowError(error); return false; }
        }

        private static void WriteLayoutFile(string path, string json)
        {
            var staging = Directory.CreateDirectory(".utmp");
            string draft = Path.Combine(staging.FullName, Guid.NewGuid().ToString("N") + ".json");
            try
            {
                File.WriteAllText(draft, json + "\n");
                if (File.Exists(path)) File.Replace(draft, path, null);
                else File.Move(draft, path);
            }
            finally
            {
                if (File.Exists(draft)) File.Delete(draft);
            }
        }

        private void ShowError(Exception error)
        {
            status = error.Message;
            statusType = MessageType.Error;
        }

        private static string RelativeFile(string path)
        {
            string prefix = BoardLayoutLoader.AssetFolder + "/";
            if (string.IsNullOrEmpty(path) || !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Choose a JSON in {BoardLayoutLoader.AssetFolder}.");
            string file = path.Substring(prefix.Length);
            BoardLayoutLoader.ResourcePath(file);
            return file;
        }

        private static bool Drawable(Vector2 center, float width, float height) =>
            MarbleRules.IsFinite(center.x) && MarbleRules.IsFinite(center.y) && MarbleRules.IsFinite(width) &&
            MarbleRules.IsFinite(height) && width > 0 && height > 0;

        private static Rect CenteredRect(Vector2 center, float width, float height) =>
            new Rect(center.x - width * .5f, center.y - height * .5f, width, height);
    }
}
