using UnityEngine;

namespace ZoneZ
{
    /// <summary>An original, quiet synthesized harmony. No recordings or external music assets.</summary>
    public sealed class MenuAmbience : MonoBehaviour
    {
        static AudioClip loop;
        AudioSource source;
        public bool IsPlaying => source && source.isPlaying;

        public static MenuAmbience Create(Transform parent)
        {
            var node=WorldArt.Group(parent,"Original menu harmony");
            var ambience=node.gameObject.AddComponent<MenuAmbience>(); ambience.Build(); return ambience;
        }

        void Build()
        {
            if(!loop) loop=Compose();
            source=gameObject.AddComponent<AudioSource>(); source.clip=loop; source.loop=true;
            source.playOnAwake=false; source.spatialBlend=0; source.priority=230; source.volume=0;
        }

        void Update()
        {
            var root=GameRoot.Instance;
            bool inMenu=root && root.Match && root.Match.Phase==MatchPhase.Menu;
            float volume=inMenu?Mathf.Clamp01(root.Profile?.menuMusicVolume??.3f):0;
            // Stop immediately on preparation/playing/results so music never overlaps a live match.
            if(!inMenu || volume<=.0001f)
            {
                if(source.isPlaying) source.Stop();
                source.volume=0; return;
            }
            if(!source.isPlaying) source.Play();
            source.volume=Mathf.MoveTowards(source.volume,volume,Time.unscaledDeltaTime*.3f);
        }

        static AudioClip Compose()
        {
            const int rate=22050, duration=16, count=rate*duration;
            var data=new float[count];
            int[,] harmony={{52,59,62},{48,55,59},{55,59,62},{50,57,60}};
            int[] melody={71,67,66,62,64,67,62,59};
            for(int i=0;i<count;i++)
            {
                float time=i/(float)rate;
                int chord=Mathf.Min(3,(int)(time/4)); float local=time-chord*4;
                float padEnvelope=Mathf.Sin(local*.25f*Mathf.PI); padEnvelope*=padEnvelope;
                float sample=0;
                for(int voice=0;voice<3;voice++)
                {
                    float phase=local*Frequency(harmony[chord,voice])*Mathf.PI*2;
                    sample+=(Mathf.Sin(phase)+.12f*Mathf.Sin(phase*2))*.028f*padEnvelope;
                }
                int note=Mathf.Min(7,(int)(time/2)); float noteTime=time-note*2;
                float bellEnvelope=Mathf.Sin(noteTime*.5f*Mathf.PI)*Mathf.Exp(-noteTime*2.5f);
                float bellPhase=noteTime*Frequency(melody[note])*Mathf.PI*2;
                sample+=(Mathf.Sin(bellPhase)+.16f*Mathf.Sin(bellPhase*3))*.045f*bellEnvelope;
                float seam=Mathf.SmoothStep(0,1,Mathf.Clamp01(Mathf.Min(time,duration-time)/.15f));
                data[i]=sample*seam;
            }
            var clip=AudioClip.Create("Last Shift - original menu harmony",count,1,rate,false);
            clip.SetData(data,0); return clip;
        }

        static float Frequency(int midi) => 440*Mathf.Pow(2,(midi-69)/12f);
    }
}
