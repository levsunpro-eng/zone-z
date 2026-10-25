using System.Collections.Generic;
using UnityEngine;

namespace ZoneZ
{
    public sealed class WeaponPresentation : MonoBehaviour
    {
        public Transform Muzzle, Magazine, Slide, Pump, SupportHand, TriggerHand, Suppressor;
        public Transform TriggerGrip, SupportGrip, ReloadGrip;
        public float Length=.8f, BareMuzzleZ;
        public bool FirstPerson, Melee, Electric, SingleShellReload;
        public string WeaponId;
        public Vector3 MagazineRemoval=Vector3.down*.20f;
        internal readonly List<Object> OwnedResources=new List<Object>();
        internal WeaponHandProfile HandProfile;
        Vector3 initialPosition, magazinePosition, slidePosition, pumpPosition, handPosition, triggerPosition, muzzlePosition;
        Quaternion initialRotation, handRotation, magazineRotation;
        float kick, inspection, reload, muzzleLife, draw=1, alternatePose, elapsed, shotAge=10;
        Transform flash,reloadRound;
        Vector3 magazineContact;
        Vector3 flashScale;
        Light flashLight;
        bool initialized, emptyReload, alternate, alternateKnown;
        public float ReloadAmount => reload;
        public float SupportReleaseAmount { get; private set; }

        internal void Finish()
        {
            // Keep the anatomical grip in frame at the game's default 60-degree camera FOV.
            // Applied by the shared factory, so gameplay and review use the same framing.
            if(FirstPerson) transform.localPosition+=new Vector3(-.06f,.08f,.20f);
            if(FirstPerson && HandProfile!=null && HandProfile.Compact)
                transform.localPosition+=Melee?new Vector3(-.035f,.04f,-.025f):new Vector3(-.055f,.085f,.035f);
            initialPosition=transform.localPosition; initialRotation=transform.localRotation;
            if(Magazine) { magazinePosition=Magazine.localPosition; magazineRotation=Magazine.localRotation; magazineContact=Magazine.InverseTransformPoint(ReloadGrip?ReloadGrip.position:PartCenter(Magazine)); }
            if(Slide) slidePosition=Slide.localPosition;
            if(Pump) pumpPosition=Pump.localPosition;
            if(SupportHand) { handPosition=SupportHand.localPosition; handRotation=SupportHand.localRotation; }
            if(TriggerHand) triggerPosition=TriggerHand.localPosition;
            foreach(var part in new[] { Magazine, SupportHand, TriggerHand }) if(part) WorldArt.CombineRigidParts(part,OwnedResources);
            CombineStaticParts();
            if(FirstPerson && SupportHand && (SingleShellReload || HandProfile?.Family==HandFamily.Revolver)) CreateReloadRound();
            SupportReleaseAmount=HandProfile?.SupportRelease??0;
            initialized=true;
            if(!Muzzle || Melee) return;
            muzzlePosition=Muzzle.localPosition;
            var mat=WorldArt.Mat(Electric?"Electrical muzzle cyan":"Muzzle gold",Electric?new Color(.18f,.78f,1):new Color(1,.69f,.17f),0,.1f);
            mat.EnableKeyword("_EMISSION"); mat.SetColor("_EmissionColor",Electric?new Color(.3f,2,3):new Color(3,1.4f,.15f));
            flash=WorldArt.Shape(Muzzle,"Muzzle flash",PrimitiveType.Sphere,new Vector3(0,0,.035f),new Vector3(.09f,.065f,.18f),mat).transform;
            flashScale=flash.localScale;
            flash.gameObject.SetActive(false);
            if(FirstPerson)
            {
                flashLight=Muzzle.gameObject.AddComponent<Light>(); flashLight.color=Electric?Color.cyan:new Color(1,.6f,.2f);
                flashLight.range=3; flashLight.intensity=0; flashLight.shadows=LightShadows.None;
            }
        }

        public void Fire() { kick=1; shotAge=0; if(!Melee) muzzleLife=GameRoot.Instance?.Profile?.reducedFlashes==true ? .018f : .045f; }
        public void SetReload(float progress) => SetReload(progress,false);
        public void SetReload(float progress,bool empty) { reload=Mathf.Clamp01(progress); emptyReload=empty; }
        public void Inspect() { inspection=2.6f; }
        public void SetAlternate(bool value)
        {
            if(alternateKnown && alternate!=value) alternatePose=1;
            alternate=value; alternateKnown=true;
            if(!Suppressor) return;
            Suppressor.gameObject.SetActive(value);
            if(Muzzle) Muzzle.localPosition=value?muzzlePosition:new Vector3(muzzlePosition.x,muzzlePosition.y,BareMuzzleZ);
        }

        void LateUpdate()
        {
            if(!initialized) return;
            var profile=GameRoot.Instance?.Profile;
            float motion=FirstPerson?Mathf.Clamp01(profile?.viewMotion??1):1;
            bool reducedFlashes=profile?.reducedFlashes??false;
            float dt=GameRoot.Instance && GameRoot.Instance.Paused && FirstPerson?0:Time.unscaledDeltaTime;
            elapsed+=dt; kick=Mathf.MoveTowards(kick,0,dt*(Melee?3.8f:9));
            shotAge+=dt;
            draw=Mathf.MoveTowards(draw,0,dt*3.6f); alternatePose=Mathf.MoveTowards(alternatePose,0,dt*3);
            muzzleLife=Mathf.Max(0,muzzleLife-dt); inspection=Mathf.Max(0,inspection-dt);
            if(flash)
            {
                flash.gameObject.SetActive(muzzleLife>0);
                flash.localScale=flashScale*(reducedFlashes ? .35f : 1);
                if(muzzleLife>0) flash.localRotation=reducedFlashes?Quaternion.identity:Quaternion.Euler(0,0,Time.frameCount*131);
            }
            if(flashLight) flashLight.intensity=muzzleLife>0&&!reducedFlashes?1.4f:0;
            float reloadPose=reload>0&&reload<1?Mathf.Sin(reload*Mathf.PI):0;
            float inspectPose=inspection>0?Mathf.Sin((2.6f-inspection)/2.6f*Mathf.PI):0;
            float extract=reload>0&&reload<1?Mathf.SmoothStep(0,1,Mathf.InverseLerp(.12f,.35f,reload))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.62f,.82f,reload))):0;
            bool fittedAk=WeaponId=="weapon_ak47" && Magazine && Magazine.name.StartsWith("Imported AK");
            var family=HandProfile?.Family??HandFamily.LongGun;
            bool pistol=family==HandFamily.Pistol || family==HandFamily.Revolver || family==HandFamily.DualPistols;
            bool shell=SingleShellReload || family==HandFamily.ShellFed;
            bool dual=family==HandFamily.DualPistols;
            if(fittedAk) extract=reload>0&&reload<1?Mathf.SmoothStep(0,1,Mathf.InverseLerp(.20f,.40f,reload))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.54f,.72f,reload))):0;
            float chamber=emptyReload&&reload>.82f&&reload<1?Mathf.Sin(Mathf.InverseLerp(.82f,1,reload)*Mathf.PI):0;
            if(FirstPerson)
            {
                Vector3 idle=new Vector3(Mathf.Sin(elapsed*1.6f)*.0018f,Mathf.Sin(elapsed*2.2f)*.002f,0);
                // This factor only changes the view model. Camera recoil and shot simulation are owned by Combat.
                float visibleKick=kick*Mathf.Lerp(.35f,1,motion);
                float lift=fittedAk?.19f:pistol?.11f:shell?.075f:.12f;
                transform.localPosition=initialPosition+(idle+new Vector3(-inspectPose*.08f-reloadPose*(fittedAk?.11f:.055f),reloadPose*lift-draw*.23f,reloadPose*(fittedAk?.08f:.035f)))*motion+new Vector3(Melee?visibleKick*.14f:0,0,-visibleKick*(Melee?-.08f:.055f));
                transform.localRotation=initialRotation*Quaternion.Euler(-visibleKick*(Melee?37:5)+(-reloadPose*(fittedAk?8:pistol?11:12)+draw*35)*motion,(inspectPose*-36-reloadPose*(fittedAk?12:pistol?14:5))*motion+(Melee?visibleKick*49:0),(reloadPose*(fittedAk?22:pistol?27:20)+inspectPose*-26-alternatePose*15)*motion);
            }
            else if(inspection>0) transform.localRotation=initialRotation*Quaternion.Euler(0,inspectPose*38,inspectPose*-12);
            else transform.localRotation=initialRotation;
            if(Magazine) Magazine.localPosition=magazinePosition+(SingleShellReload?Vector3.zero:MagazineRemoval*extract);
            if(fittedAk) Magazine.localRotation=magazineRotation*Quaternion.Euler(-extract*12,0,0);
            else if(Magazine) Magazine.localRotation=magazineRotation*Quaternion.Euler(family==HandFamily.Revolver?0:-extract*5,0,family==HandFamily.Revolver?-extract*15:0);
            if(Slide) Slide.localPosition=slidePosition+Vector3.back*(kick+chamber)*.025f;
            float pumpStroke=Pump?Pulse(shotAge,.075f,.22f,.43f)*.085f:0;
            if(Pump) Pump.localPosition=pumpPosition+Vector3.back*pumpStroke;
            if(SupportHand)
            {
                Vector3 reloadHand=SingleShellReload?new Vector3(-.09f,-.16f+Mathf.Sin(reload*Mathf.PI*2)*.04f,.08f):new Vector3(-.04f,-.19f,.07f)+MagazineRemoval*extract;
                SupportHand.localPosition=Vector3.Lerp(handPosition,reloadHand,reloadPose)+new Vector3(-inspectPose*.016f*motion,0,-chamber*.06f);
                SupportHand.localRotation=handRotation*Quaternion.Euler(reloadPose*-30*motion,0,reloadPose*22*motion);
                if(fittedAk) {
                    float reach=reload>0&&reload<1?Mathf.SmoothStep(0,1,Mathf.InverseLerp(.025f,.18f,reload))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(.74f,.97f,reload))):0;
                    SupportHand.localPosition=Vector3.Lerp(handPosition,new Vector3(-.043f,-.16f,.095f)+MagazineRemoval*extract,reach);
                    SupportHand.localRotation=Quaternion.Slerp(handRotation,Quaternion.LookRotation(Vector3.right,Vector3.up),reach);
                }
                else if(HandProfile!=null) AnimateSupport(family,shell,dual,extract,chamber,pumpStroke,inspectPose,motion);
            }
            if(TriggerHand) TriggerHand.localPosition=triggerPosition+Vector3.back*(kick*.009f);
            UpdateHandRelease(family,shell,dual);
        }

        void AnimateSupport(HandFamily family,bool shell,bool dual,float extract,float chamber,float pumpStroke,float inspectPose,float motion)
        {
            bool active=reload>0 && reload<1;
            SupportHand.localPosition=handPosition+new Vector3(-inspectPose*.016f*motion,0,HandProfile.SupportOnPump?-pumpStroke:0);
            SupportHand.localRotation=handRotation;
            if(Melee)
            {
                SupportHand.localPosition+=new Vector3(-.06f,-.045f,-.04f)*kick;
                SupportHand.localRotation=handRotation*Quaternion.Euler(0,-kick*12,-kick*7);
                return;
            }
            if(dual)
            {
                // The left hand keeps its own pistol; it must never reach through the right gun.
                SupportHand.localPosition+=Vector3.back*(kick*.009f);
                return;
            }
            if(!active)return;
            Quaternion targetRotation=HandProfile.LoadRotation;
            Vector3 contact=HandProfile.LoadPosition;
            float reach=Window(reload,.035f,.20f,.78f,.98f);
            if(shell)
            {
                // Each gameplay shell interval supplies one visual insert cycle.
                float insert=Window(reload,.16f,.38f,.62f,.88f);
                Vector3 pickup=handPosition+new Vector3(-.09f,-.16f,-.12f);
                var port=ReloadGrip?transform.InverseTransformPoint(ReloadGrip.position):HandProfile.LoadPosition;
                Vector3 atPort=port-HandProfile.LoadRotation*HandProfile.SupportPalm;
                SupportHand.localPosition=Vector3.Lerp(handPosition,Vector3.Lerp(pickup,atPort,insert),reach);
                SupportHand.localRotation=Quaternion.Slerp(handRotation,targetRotation,reach);
                return;
            }
            if(Magazine) contact=transform.InverseTransformPoint(Magazine.TransformPoint(magazineContact))+Vector3.left*.018f;
            if(family==HandFamily.Revolver)contact+=new Vector3(-.045f,0,-.025f);
            Vector3 target=contact-targetRotation*HandProfile.SupportPalm;
            SupportHand.localPosition=Vector3.Lerp(handPosition,target,reach);
            SupportHand.localRotation=Quaternion.Slerp(handRotation,targetRotation,reach);
            if(chamber>0 && Slide && family!=HandFamily.Revolver)
            {
                Vector3 chargingContact=transform.InverseTransformPoint(Slide.position)+new Vector3(-.036f,.006f,-.015f);
                Vector3 chargingWrist=chargingContact-targetRotation*HandProfile.SupportPalm;
                SupportHand.localPosition=Vector3.Lerp(SupportHand.localPosition,chargingWrist,chamber*.75f);
                SupportHand.localRotation=Quaternion.Slerp(SupportHand.localRotation,targetRotation,chamber*.75f);
            }
        }

        void UpdateHandRelease(HandFamily family,bool shell,bool dual)
        {
            bool active=reload>0 && reload<1;
            float baseline=HandProfile?.SupportRelease??0;
            float release=active&&!dual&&!Melee?Mathf.Max(Pulse(reload,.015f,.10f,.21f),Pulse(reload,.74f,.83f,.96f))*72:0;
            if(active&&shell)release=Mathf.Max(release,Window(reload,.37f,.49f,.63f,.74f)*38);
            SupportReleaseAmount=Mathf.Max(baseline,release);
            if(reloadRound)reloadRound.gameObject.SetActive(active && (shell?reload>.14f && reload<.64f:family==HandFamily.Revolver && reload>.28f && reload<.73f));
        }

        void CreateReloadRound()
        {
            reloadRound=WorldArt.Group(SupportHand,"Presentation cartridge",new Vector3(0,.095f,.045f),new Vector3(0,0,90));
            var brass=WorldArt.Mat("Reload cartridge brass",new Color(.49f,.34f,.12f),.72f,.40f);
            var casing=WorldArt.Mat("Reload shell casing",new Color(.36f,.055f,.035f),.02f,.25f);
            WorldArt.Cylinder(reloadRound,"Cartridge case",Vector3.zero,.010f,.052f,SingleShellReload?casing:brass);
            WorldArt.Cylinder(reloadRound,"Cartridge rim",new Vector3(0,-.027f,0),.011f,.009f,brass);
            reloadRound.gameObject.SetActive(false);
        }

        static Vector3 PartCenter(Transform part)
        {
            var renderers=part.GetComponentsInChildren<Renderer>();
            if(renderers.Length==0)return part.position;
            var bounds=renderers[0].bounds;
            for(int i=1;i<renderers.Length;i++)bounds.Encapsulate(renderers[i].bounds);
            return bounds.center;
        }
        static float Window(float x,float start,float inEnd,float outStart,float end) => Mathf.SmoothStep(0,1,Mathf.InverseLerp(start,inEnd,x))*(1-Mathf.SmoothStep(0,1,Mathf.InverseLerp(outStart,end,x)));
        static float Pulse(float x,float start,float peak,float end) => Window(x,start,peak,peak,end);

        bool Animated(Transform child)
        {
            return IsPart(child,Magazine)||IsPart(child,Slide)||IsPart(child,Pump)||IsPart(child,SupportHand)||IsPart(child,TriggerHand)||IsPart(child,Suppressor);
        }
        static bool IsPart(Transform child,Transform root) => root && (child==root || child.IsChildOf(root));

        void CombineStaticParts()
        {
            var groups=new Dictionary<Material,List<MeshFilter>>();
            foreach(var filter in GetComponentsInChildren<MeshFilter>())
            {
                var renderer=filter.GetComponent<MeshRenderer>();
                if(!renderer || !renderer.enabled || Animated(filter.transform) || !filter.sharedMesh || !renderer.sharedMaterial) continue;
                if(!groups.TryGetValue(renderer.sharedMaterial,out var group)) { group=new List<MeshFilter>(); groups.Add(renderer.sharedMaterial,group); }
                group.Add(filter);
            }
            foreach(var group in groups)
            {
                if(group.Value.Count<2) continue;
                var combine=new CombineInstance[group.Value.Count];
                for(int i=0;i<combine.Length;i++)
                {
                    combine[i]=new CombineInstance{mesh=group.Value[i].sharedMesh,transform=transform.worldToLocalMatrix*group.Value[i].transform.localToWorldMatrix};
                    group.Value[i].GetComponent<MeshRenderer>().enabled=false;
                }
                var mesh=new Mesh{name="Batched weapon "+group.Key.name}; mesh.indexFormat=UnityEngine.Rendering.IndexFormat.UInt32;
                mesh.CombineMeshes(combine,true,true); mesh.RecalculateBounds(); OwnedResources.Add(mesh);
                var node=WorldArt.Group(transform,"Batched "+group.Key.name);
                node.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
                node.gameObject.AddComponent<MeshRenderer>().sharedMaterial=group.Key;
            }
        }

        void OnDestroy()
        {
            foreach(var resource in OwnedResources) Dispose(resource);
            foreach(var filter in GetComponentsInChildren<MeshFilter>(true))
                if(filter.sharedMesh && filter.sharedMesh.name.EndsWith(" mesh")) Dispose(filter.sharedMesh);
        }
        static void Dispose(Object resource) { if(!resource) return; if(Application.isPlaying) Destroy(resource); else DestroyImmediate(resource); }
    }
}
