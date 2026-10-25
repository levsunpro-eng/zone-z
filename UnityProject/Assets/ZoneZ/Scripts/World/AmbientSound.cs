using UnityEngine;

namespace ZoneZ
{
    /// <summary>Original synthesized environmental sound; never emits combat hearing events.</summary>
    public sealed class AmbientSound : MonoBehaviour
    {
        static AudioClip windClip, birdClip;
        AudioSource wind, birds;
        float nextBird;

        public static void Create(Transform parent)
        {
            var node=WorldArt.Group(parent,"Zoo ambient sound");
            node.gameObject.AddComponent<AmbientSound>().Build();
        }

        void Build()
        {
            if(!windClip) windClip=Wind();
            if(!birdClip) birdClip=Bird();
            wind=gameObject.AddComponent<AudioSource>(); wind.clip=windClip; wind.loop=true; wind.playOnAwake=false;
            wind.spatialBlend=0; wind.priority=220; wind.volume=0;
            var perch=WorldArt.Group(transform,"Distant bird sound",new Vector3(55,2.6f,61));
            birds=perch.gameObject.AddComponent<AudioSource>(); birds.clip=birdClip; birds.playOnAwake=false;
            birds.spatialBlend=1; birds.minDistance=3; birds.maxDistance=38; birds.rolloffMode=AudioRolloffMode.Logarithmic; birds.priority=210;
            nextBird=12;
            if(Application.isPlaying) wind.Play();
        }

        void Update()
        {
            float volume=GameRoot.Instance && GameRoot.Instance.Profile!=null?GameRoot.Instance.Profile.ambientVolume:.5f;
            wind.volume=.07f*volume; birds.volume=.12f*volume;
            nextBird-=Time.deltaTime;
            if(nextBird<=0) { nextBird=15+Mathf.Sin(Time.time*.07f)*4; if(volume>.001f) birds.Play(); }
        }

        static AudioClip Wind()
        {
            const int rate=22050, count=rate*12;
            var data=new float[count]; var random=new System.Random(7383);
            float low=0;
            for(int i=0;i<count;i++)
            {
                low=Mathf.Lerp(low,(float)random.NextDouble()*2-1,.025f);
                float envelope=.65f+.22f*Mathf.Sin(i/(float)count*Mathf.PI*4)+.08f*Mathf.Sin(i/(float)count*Mathf.PI*10);
                data[i]=low*envelope;
            }
            // Smoothly reach zero at both loop ends, eliminating a discontinuous seam.
            for(int i=0;i<rate/4;i++) { float fade=Mathf.SmoothStep(0,1,i/(float)(rate/4)); data[i]*=fade; data[count-1-i]*=fade; }
            var clip=AudioClip.Create("Original zoo wind",count,1,rate,false); clip.SetData(data,0); return clip;
        }

        static AudioClip Bird()
        {
            const int rate=22050, count=rate*2;
            var data=new float[count]; float phase=0;
            for(int i=0;i<count;i++)
            {
                float time=i/(float)rate, syllable=Mathf.Repeat(time,.47f), envelope=Mathf.Sin(Mathf.Clamp01(syllable/.30f)*Mathf.PI);
                envelope=syllable>.30f?0:envelope*envelope;
                phase+=Mathf.PI*2*(410+Mathf.Sin(time*13)*75)/rate;
                float edge=Mathf.Clamp01(Mathf.Min(time/.035f,(2-time)/.12f));
                data[i]=(Mathf.Sin(phase)+Mathf.Sin(phase*2)*.16f)*envelope*.18f*edge;
            }
            var clip=AudioClip.Create("Original pigeon call",count,1,rate,false); clip.SetData(data,0); return clip;
        }
    }
}
