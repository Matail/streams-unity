// 배경음악(Resources/Audio/bgm, Suno 로 만든 곡)과 효과음.
// 효과음은 파일 없이 코드로 합성한 8비트 소리 — 도트 화면과 어울리고 빌드가 가볍다.
using UnityEngine;

namespace Streams.Presentation
{
    enum Sfx { Place, Ai, Win, Lose }

    static class Sound
    {
        const int Rate = 44100;
        static AudioSource bgm, sfx;
        static AudioClip[] clips;

        static void Init()
        {
            if (sfx != null) return;
            var go = new GameObject("Sound");
            Object.DontDestroyOnLoad(go);
            sfx = go.AddComponent<AudioSource>();
            bgm = go.AddComponent<AudioSource>();
            bgm.loop = true;
            bgm.volume = 0.35f;
            bgm.clip = Resources.Load<AudioClip>("Audio/bgm");
            clips = new[]
            {
                Tones("place", 0.06f, 880),
                Tones("ai", 0.06f, 587),
                Tones("win", 0.12f, 523, 659, 784, 1047),
                Tones("lose", 0.16f, 392, 330, 262),
            };
        }

        /// <summary>배경음악 시작 (이미 돌고 있으면 그대로).</summary>
        public static void Music()
        {
            Init();
            if (bgm.clip != null && !bgm.isPlaying) bgm.Play();
        }

        public static void Play(Sfx s)
        {
            Init();
            sfx.PlayOneShot(clips[(int)s], 0.5f);
        }

        /// <summary>사각파 음을 차례로 이어 붙인 짧은 소리. 음마다 끝을 줄여 딸깍거림을 없앤다.</summary>
        static AudioClip Tones(string name, float noteSec, params float[] hz)
        {
            int per = Mathf.RoundToInt(noteSec * Rate);
            var data = new float[per * hz.Length];
            for (int n = 0; n < hz.Length; n++)
                for (int i = 0; i < per; i++)
                {
                    float t = (float)i / Rate;
                    float square = Mathf.Sign(Mathf.Sin(2 * Mathf.PI * hz[n] * t));
                    data[n * per + i] = square * 0.25f * (1f - (float)i / per);
                }
            var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
