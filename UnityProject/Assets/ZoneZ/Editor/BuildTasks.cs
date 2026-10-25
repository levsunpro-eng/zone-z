using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace ZoneZ.Editor
{
    public static class BuildTasks
    {
        const string ScenePath="Assets/ZoneZ/Scenes/Boot.unity";
        [MenuItem("Zone Z/1. Prepare project")]
        public static void Setup()
        {
            Directory.CreateDirectory("Assets/ZoneZ/Scenes");
            Directory.CreateDirectory("Assets/ZoneZ/Rendering");
            Directory.CreateDirectory("Assets/ZoneZ/Resources/ShaderSupport");
            Directory.CreateDirectory("Assets/ZoneZ/Resources/World");
            SyncBalance();
            ConfigureTextures();
            PlayerSettings.companyName="Zone Z Studio";
            PlayerSettings.productName="Зона Z";
            PlayerSettings.bundleVersion="0.2.0";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone,"games.zonez.lastshift");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone,ScriptingImplementation.Mono2x);
            PlayerSettings.SetApiCompatibilityLevel(NamedBuildTarget.Standalone,ApiCompatibilityLevel.NET_Standard);
            PlayerSettings.colorSpace=ColorSpace.Linear;
            PlayerSettings.defaultScreenWidth=1920;PlayerSettings.defaultScreenHeight=1080;
            PlayerSettings.fullScreenMode=FullScreenMode.FullScreenWindow;
            PlayerSettings.runInBackground=true; PlayerSettings.resizableWindow=true;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64,false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,new[]{GraphicsDeviceType.Direct3D11});
            var playerSettings=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
            var input=playerSettings.FindProperty("activeInputHandler");if(input!=null){input.intValue=0;playerSettings.ApplyModifiedPropertiesWithoutUndo();}
            ConfigureInput();
            var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/ZoneZ/Rendering/ForwardRenderer.asset");
            if(!renderer){renderer=ScriptableObject.CreateInstance<UniversalRendererData>();AssetDatabase.CreateAsset(renderer,"Assets/ZoneZ/Rendering/ForwardRenderer.asset");}
            ConfigureAmbientOcclusion(renderer);
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/ZoneZ/Rendering/ZoneZ_URP.asset");
            if(!pipeline){pipeline=UniversalRenderPipelineAsset.Create(renderer);AssetDatabase.CreateAsset(pipeline,"Assets/ZoneZ/Rendering/ZoneZ_URP.asset");}
            pipeline.msaaSampleCount=4;pipeline.renderScale=1;pipeline.shadowDistance=70;pipeline.shadowCascadeCount=2;
            pipeline.supportsHDR=true;
            pipeline.supportsCameraDepthTexture=true;
            pipeline.colorGradingMode=ColorGradingMode.HighDynamicRange;
            var lightingSettings=new SerializedObject(pipeline);
            lightingSettings.FindProperty("m_SoftShadowsSupported").boolValue=true;
            lightingSettings.FindProperty("m_AdditionalLightsRenderingMode").intValue=(int)LightRenderingMode.PerPixel;
            lightingSettings.ApplyModifiedPropertiesWithoutUndo();
            GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
            QualitySettings.vSyncCount=1; QualitySettings.anisotropicFiltering=AnisotropicFiltering.Enable;
            QualitySettings.lodBias=1.4f;
            foreach(string shaderName in new[]{"Universal Render Pipeline/Lit","Universal Render Pipeline/Unlit","Skybox/Procedural","Sprites/Default","GUI/Text Shader"}) {
                var shader=Shader.Find(shaderName);if(!shader)throw new InvalidOperationException("Missing shader "+shaderName);
                string file="Assets/ZoneZ/Resources/ShaderSupport/"+shaderName.Replace('/','_').Replace(' ','_')+".mat";
                if(!File.Exists(file))AssetDatabase.CreateAsset(new Material(shader),file);
            }
            const string emissionPath="Assets/ZoneZ/Resources/ShaderSupport/LitEmission.mat";
            if(!File.Exists(emissionPath)) {
                var emission=new Material(Shader.Find("Universal Render Pipeline/Lit"));emission.EnableKeyword("_EMISSION");emission.SetColor("_EmissionColor",Color.white);
                AssetDatabase.CreateAsset(emission,emissionPath);
            }
            const string foliagePath="Assets/ZoneZ/Resources/ShaderSupport/FoliageCutout.mat";
            var foliage=AssetDatabase.LoadAssetAtPath<Material>(foliagePath);
            if(!foliage){foliage=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(foliage,foliagePath);}
            foliage.EnableKeyword("_ALPHATEST_ON");foliage.SetFloat("_AlphaClip",1);foliage.SetFloat("_Cutoff",.4f);foliage.SetFloat("_Cull",0);
            foliage.SetOverrideTag("RenderType","TransparentCutout");foliage.renderQueue=2450;
            foliage.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ZoneZ/Resources/Textures/oak-foliage.png");EditorUtility.SetDirty(foliage);
            ConfigureWorldLook();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            new GameObject("Zone Z").AddComponent<GameRoot>();
            EditorSceneManager.SaveScene(scene,ScenePath);
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};
            EditorUtility.SetDirty(pipeline);AssetDatabase.SaveAssets();AssetDatabase.Refresh();
            Debug.Log("ZONEZ_SETUP_OK");
        }

        public static void SyncBalance()
        {
            string source=Path.GetFullPath(Path.Combine(Application.dataPath,"../../data"));
            string target=Path.Combine(Application.dataPath,"ZoneZ/Resources/Balance");Directory.CreateDirectory(target);
            foreach(string file in Directory.GetFiles(source,"*.json"))File.Copy(file,Path.Combine(target,Path.GetFileName(file)),true);
            AssetDatabase.Refresh();
        }

        static void ConfigureTextures()
        {
            const string folder="Assets/ZoneZ/Resources/Textures";
            if(!Directory.Exists(folder))return;
            foreach(string file in Directory.GetFiles(folder,"*.png")) {
                var importer=AssetImporter.GetAtPath(file.Replace('\\','/')) as TextureImporter;
                if(importer==null)continue;
                bool normal=file.EndsWith("-normal.png",StringComparison.OrdinalIgnoreCase);
                bool occlusion=file.EndsWith("-ao.png",StringComparison.OrdinalIgnoreCase);
                importer.textureType=normal?TextureImporterType.NormalMap:TextureImporterType.Default;
                importer.sRGBTexture=!normal&&!occlusion;importer.mipmapEnabled=true;
                if(normal){importer.convertToNormalmap=false;importer.flipGreenChannel=false;}
                importer.maxTextureSize=1024;importer.anisoLevel=8;importer.filterMode=FilterMode.Trilinear;
                importer.wrapMode=file.Contains("oak-foliage")?TextureWrapMode.Clamp:TextureWrapMode.Repeat;
                importer.alphaSource=TextureImporterAlphaSource.FromInput;importer.alphaIsTransparency=file.Contains("oak-foliage");
                importer.textureCompression=TextureImporterCompression.CompressedHQ;importer.SaveAndReimport();
            }
        }

        static void ConfigureAmbientOcclusion(UniversalRendererData renderer)
        {
            var ao=renderer.rendererFeatures.OfType<ScreenSpaceAmbientOcclusion>().FirstOrDefault();
            if(!ao)
            {
                ao=ScriptableObject.CreateInstance<ScreenSpaceAmbientOcclusion>();
                ao.name="Masonry and ground contact shadows";
                AssetDatabase.AddObjectToAsset(ao,renderer);
                renderer.rendererFeatures.Add(ao);
            }
            var settings=new SerializedObject(ao);
            var state=settings.FindProperty("m_Settings");
            state.FindPropertyRelative("AOMethod").intValue=1;
            state.FindPropertyRelative("Downsample").boolValue=true;
            state.FindPropertyRelative("AfterOpaque").boolValue=false;
            state.FindPropertyRelative("Source").intValue=0;
            state.FindPropertyRelative("NormalSamples").intValue=1;
            state.FindPropertyRelative("Intensity").floatValue=.85f;
            state.FindPropertyRelative("DirectLightingStrength").floatValue=.15f;
            state.FindPropertyRelative("Radius").floatValue=.38f;
            state.FindPropertyRelative("Samples").intValue=2;
            state.FindPropertyRelative("BlurQuality").intValue=1;
            state.FindPropertyRelative("Falloff").floatValue=55;
            settings.ApplyModifiedPropertiesWithoutUndo();
            ao.SetActive(true);ao.Create();renderer.SetDirty();
            EditorUtility.SetDirty(ao);EditorUtility.SetDirty(renderer);
        }

        static void ConfigureWorldLook()
        {
            const string profilePath="Assets/ZoneZ/Resources/World/DuskLook.asset";
            var profile=AssetDatabase.LoadAssetAtPath<VolumeProfile>(profilePath);
            if(!profile){profile=ScriptableObject.CreateInstance<VolumeProfile>();AssetDatabase.CreateAsset(profile,profilePath);}
            WorldLook.Configure(profile);
            foreach(var component in profile.components)
            {
                if(!AssetDatabase.Contains(component))AssetDatabase.AddObjectToAsset(component,profile);
                EditorUtility.SetDirty(component);
            }
            EditorUtility.SetDirty(profile);
            // Serialize the exact normal/occlusion shader variant used by runtime architecture.
            const string pbrPath="Assets/ZoneZ/Resources/ShaderSupport/WorldSurfacePbr.mat";
            var pbr=AssetDatabase.LoadAssetAtPath<Material>(pbrPath);
            if(!pbr){pbr=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(pbr,pbrPath);}
            pbr.EnableKeyword("_NORMALMAP");pbr.EnableKeyword("_OCCLUSIONMAP");
            pbr.SetTexture("_BumpMap",Resources.Load<Texture2D>("Textures/weathered-concrete-normal"));
            pbr.SetTexture("_OcclusionMap",Resources.Load<Texture2D>("Textures/weathered-concrete-ao"));
            EditorUtility.SetDirty(pbr);
        }

        static void ConfigureInput()
        {
            var assets=AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/InputManager.asset");
            if(assets.Length==0)throw new InvalidOperationException("InputManager settings missing");
            var settings=new SerializedObject(assets[0]);var axes=settings.FindProperty("m_Axes");axes.arraySize=5;
            Axis(axes.GetArrayElementAtIndex(0),"Horizontal","left","right","a","d",0,0,3,3);
            Axis(axes.GetArrayElementAtIndex(1),"Vertical","down","up","s","w",0,0,3,3);
            Axis(axes.GetArrayElementAtIndex(2),"Mouse X","","","","",1,0,0,.1f);
            Axis(axes.GetArrayElementAtIndex(3),"Mouse Y","","","","",1,1,0,.1f);
            Axis(axes.GetArrayElementAtIndex(4),"Mouse ScrollWheel","","","","",1,2,0,.1f);
            settings.ApplyModifiedPropertiesWithoutUndo();
        }
        static void Axis(SerializedProperty a,string name,string negative,string positive,string altNegative,string altPositive,int type,int axis,float gravity,float sensitivity)
        {
            a.FindPropertyRelative("m_Name").stringValue=name;
            a.FindPropertyRelative("negativeButton").stringValue=negative;a.FindPropertyRelative("positiveButton").stringValue=positive;
            a.FindPropertyRelative("altNegativeButton").stringValue=altNegative;a.FindPropertyRelative("altPositiveButton").stringValue=altPositive;
            a.FindPropertyRelative("gravity").floatValue=gravity;a.FindPropertyRelative("dead").floatValue=.001f;a.FindPropertyRelative("sensitivity").floatValue=sensitivity;
            a.FindPropertyRelative("snap").boolValue=type==0;a.FindPropertyRelative("invert").boolValue=false;
            a.FindPropertyRelative("type").intValue=type;a.FindPropertyRelative("axis").intValue=axis;a.FindPropertyRelative("joyNum").intValue=0;
        }

        [MenuItem("Zone Z/2. Validate rules and data")]
        public static void Validate()
        {
            // A fresh batch editor starts with an untitled scene; additive fixtures need
            // a saved base scene. Interactive validation must preserve the user's scene.
            if(Application.isBatchMode && File.Exists(ScenePath))EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
            CoreValidation.Run();
            // Optional validators are discovered after parallel modules finish importing.
            foreach(string name in new[]{"ZoneZ.Editor.ProfileValidation","ZoneZ.Editor.CombatValidation","ZoneZ.Editor.BotDifficultyValidation","ZoneZ.Editor.WorldValidation","ZoneZ.Editor.MinimapValidation"}) {
                var type=AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetType(name)).FirstOrDefault(t=>t!=null);
                if(type==null)throw new InvalidOperationException("Required validator missing: "+name);
                type.GetMethod("Run").Invoke(null,null);
            }
            Debug.Log("ZONEZ_VALIDATION_OK");
        }

        [MenuItem("Zone Z/3. Build Windows")]
        public static void BuildWindows()
        {
            Setup();Validate();
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Builds/Animations/ZoneZ.exe"));
            var args=Environment.GetCommandLineArgs();int at=Array.IndexOf(args,"-zonez-output");if(at>=0&&at+1<args.Length)output=Path.GetFullPath(args[at+1]);
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            BuildReport report=BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes=new[]{ScenePath},locationPathName=output,target=BuildTarget.StandaloneWindows64,options=BuildOptions.None });
            Debug.Log("ZONEZ_BUILD_RESULT "+report.summary.result+" bytes="+report.summary.totalSize);
            if(report.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("Windows build failed");
        }
    }
}
