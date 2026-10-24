using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using TMPro;

namespace BloodPachinko.Rendering.Editor
{
    // Explicitly invoked editor validation; never runs on player startup or saves temporary preview values.
    public static class RetroCultValidation
    {
        public const string Work = "E:/毕设/MarbleRoguelikeGame/.utmp/retro_runtime_20261010";
        static readonly List<string> report = new List<string>();
        static void Check(bool value, string label)
        {
            if (!value) throw new InvalidOperationException(label);
            report.Add("PASS " + label);
        }

        public static Texture2D Capture(Camera camera, string name, int width=1920, int height=1080)
        {
            var oldTarget = camera.targetTexture;
            var oldActive = RenderTexture.active;
            var oldAspect = camera.aspect;
            var rt = new RenderTexture(width,height,24,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            rt.Create();
            try
            {
                camera.targetTexture = rt;
                camera.aspect = (float)width/height;
                camera.Render(); camera.Render();
                RenderTexture.active = rt;
                var image = new Texture2D(width,height,TextureFormat.RGB24,false);
                image.ReadPixels(new Rect(0,0,width,height),0,0); image.Apply();
                if (name != null) File.WriteAllBytes(Path.Combine(Work,name+".png"), image.EncodeToPNG());
                return image;
            }
            finally
            {
                camera.targetTexture = oldTarget; camera.aspect = oldAspect;
                RenderTexture.active = oldActive; rt.Release(); UnityEngine.Object.DestroyImmediate(rt);
            }
        }
        static double Difference(Texture2D a, Texture2D b, RectInt? area=null)
        {
            var aa=a.GetPixels32(); var bb=b.GetPixels32(); double sum=0; long count=0;
            var rect=area ?? new RectInt(0,0,a.width,a.height);
            for(int y=rect.yMin;y<rect.yMax;y++) for(int x=rect.xMin;x<rect.xMax;x++)
            {
                int i=y*a.width+x;
                sum+=Math.Abs(aa[i].r-bb[i].r)+Math.Abs(aa[i].g-bb[i].g)+Math.Abs(aa[i].b-bb[i].b); count+=3;
            }
            return sum/Math.Max(1,count);
        }
        static void Dispose(params Texture2D[] images) { foreach(var image in images) if(image) UnityEngine.Object.DestroyImmediate(image); }

        public static void Run()
        {
            Directory.CreateDirectory(Work); report.Clear();
            EditorSceneManager.OpenScene(RetroCultSetup.ScenePath,OpenSceneMode.Single);
            var shader=AssetDatabase.LoadAssetAtPath<Shader>("Assets/Rendering/RetroCult/RetroCult.shader");
            Check(shader && shader.isSupported,"Shader is supported");
            var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(RetroCultSetup.ProfilePath);
            Check(profile && profile.TryGet<RetroCultVolume>(out _),"Persistent profile contains custom effect");
            var originalProfile=profile;
            profile=UnityEngine.Object.Instantiate(profile);
            profile.components=originalProfile.components.Select(c=>UnityEngine.Object.Instantiate(c)).ToList();
            var volume=GameObject.Find(RetroCultSetup.RootName).GetComponent<Volume>();
            volume.sharedProfile=profile;
            profile.TryGet<RetroCultVolume>(out var settings);
            settings.jitterRate.value=0;
            var camera=Camera.main;
            camera.aspect=16f/9f;
            var position=camera.transform.position; var rotation=camera.transform.rotation;
            var lens=camera.lensShift; float fov=camera.fieldOfView;
            int originalQuality=QualitySettings.GetQualityLevel();
            try
            {
                foreach(var path in RetroCultSetup.RendererPaths)
                {
                    var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(path);
                    Check(renderer.rendererFeatures.OfType<RetroCultRendererFeature>().Count()==1,"Exactly one effect in "+path);
                }
                var rig=UnityEngine.Object.FindObjectOfType<DiegeticMachineUI>();
                Check(rig && rig.ScoreValues.Length==3,"Physical score UI exists");
                rig.ItemBoard.localPosition=rig.BoardClosedPosition;
                rig.ItemGroup.alpha=0;
                Canvas.ForceUpdateCanvases();
                foreach(var t in UnityEngine.Object.FindObjectsOfType<TMP_Text>()) t.ForceMeshUpdate();
                for(int q=0;q<QualitySettings.names.Length;q++)
                {
                    QualitySettings.SetQualityLevel(q,true);
                    var image=Capture(camera,"quality_"+q,960,540);
                    var pixels=image.GetPixels32();
                    Check(pixels.Count(p=>p.r+p.g+p.b>30)>pixels.Length/5,"Visible scene at quality "+q);
                    Dispose(image);
                }
                QualitySettings.SetQualityLevel(originalQuality,true);
                var rendered=Capture(camera,"01_方案2_Unity_1080p");
                settings.strength.value=0;
                var graded=Capture(camera,"00_仅调色_对照");
                double changed=Difference(rendered,graded);
                report.Add("Effect versus grading only mean difference (0..255): "+changed);
                Check(changed>1,"Pixel effects produce real image changes beyond color grading");

                var renderData=RetroCultSetup.RendererPaths.Select(AssetDatabase.LoadAssetAtPath<UniversalRendererData>).ToArray();
                foreach(var rd in renderData) foreach(var f in rd.rendererFeatures.OfType<RetroCultRendererFeature>()) f.SetActive(false);
                var bypass=Capture(camera,null);
                double bypassDiff=Difference(graded,bypass);
                Check(bypassDiff<.1,"Zero strength bypasses custom pass: difference="+bypassDiff);
                foreach(var rd in renderData) foreach(var f in rd.rendererFeatures.OfType<RetroCultRendererFeature>()) f.SetActive(true);
                settings.strength.value=1;

                settings.virtualHeight.value=720;
                var finer=Capture(camera,"02_调节验证_720虚拟高度");
                Check(Difference(rendered,finer)>.5,"Changing the virtual grid changes the live render");
                settings.virtualHeight.value=360;
                settings.protectText.value=false;
                var unprotected=Capture(camera,"03_文字保护关闭_检查");
                settings.protectText.value=true;
                var corners=new Vector3[4]; rig.ScoreValues[0].rectTransform.GetWorldCorners(corners);
                var points=corners.Select(camera.WorldToViewportPoint).ToArray();
                int x0=Mathf.Clamp(Mathf.FloorToInt(points.Min(p=>p.x)*1920),0,1919);
                int y0=Mathf.Clamp(Mathf.FloorToInt(points.Min(p=>p.y)*1080),0,1079);
                int x1=Mathf.Clamp(Mathf.CeilToInt(points.Max(p=>p.x)*1920),x0+1,1920);
                int y1=Mathf.Clamp(Mathf.CeilToInt(points.Max(p=>p.y)*1080),y0+1,1080);
                var region=new RectInt(x0,y0,x1-x0,y1-y0);
                double protectedError=Difference(rendered,graded,region), rawError=Difference(unprotected,graded,region);
                report.Add("Score text rectangle="+region+" protected error="+protectedError+" unprotected error="+rawError);
                Check(protectedError<rawError*.7,"Text protection follows the correct screen region");

                var high=Capture(camera,"04_方案2_Unity_4K",3840,2160);
                var returned=Capture(camera,"05_4K切回1080p");
                double resizeDiff=Difference(rendered,returned);
                Check(resizeDiff<.2,"4K -> 1080p RTHandle resize remains correct: difference="+resizeDiff);
                settings.jitterRate.value=6;
                var dynamicA=Capture(camera,"06_动态采样_A");
                System.Threading.Thread.Sleep(200);
                var dynamicB=Capture(camera,"07_动态采样_B");
                Check(Difference(dynamicA,dynamicB)>.1,"Runtime clock animates pixel edges and grain");
                Check(camera.transform.position==position && camera.transform.rotation==rotation && camera.lensShift==lens && Mathf.Approximately(camera.fieldOfView,fov),"Camera framing is unchanged");
                foreach(var msg in ShaderUtil.GetShaderMessages(shader))
                    Check(msg.severity!=UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error,"Shader compile: "+msg.message);
                Dispose(rendered,graded,bypass,finer,unprotected,high,returned,dynamicA,dynamicB);
                report.Add("RETRO_CULT_RENDER_VALIDATION_OK");
            }
            finally
            {
                volume.sharedProfile=originalProfile;
                QualitySettings.SetQualityLevel(originalQuality,true);
                foreach(var c in profile.components) UnityEngine.Object.DestroyImmediate(c);
                UnityEngine.Object.DestroyImmediate(profile);
                File.WriteAllLines(Path.Combine(Work,"render-validation.txt"),report);
                // Deliberately do not save the scene, test profile or preview state.
            }
            Debug.Log("RETRO_CULT_RENDER_VALIDATION_OK");
        }
    }
}
