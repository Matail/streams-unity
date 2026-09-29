// 서버에 보낼 x-streams-client 헤더, 서버 주소, 익명 플레이어 id.
using System;
using UnityEngine;

namespace Streams.Net
{
    public static class ClientInfo
    {
        /// <summary>예: unity-webgl/0.1.0. 형식은 docs/streams-api.md.</summary>
        public static string Header => $"{Kind}/{Version}";

        static string Kind
        {
            get
            {
                if (Application.isEditor) return "unity-editor";
                switch (Application.platform)
                {
                    case RuntimePlatform.WebGLPlayer: return "unity-webgl";
                    case RuntimePlatform.WindowsPlayer: return "unity-windows";
                    case RuntimePlatform.OSXPlayer: return "unity-mac";
                    case RuntimePlatform.Android: return "unity-android";
                    case RuntimePlatform.IPhonePlayer: return "unity-ios";
                    default: return "unity-other";
                }
            }
        }

        // 헤더 버전에 쓸 수 있는 글자만 남긴다 (서버가 형식이 틀리면 NULL 로 기록함)
        static string Version
        {
            get
            {
                var v = System.Text.RegularExpressions.Regex.Replace(Application.version ?? "", "[^0-9A-Za-z.+-]", "");
                return v.Length == 0 ? "0" : v.Length > 32 ? v.Substring(0, 32) : v;
            }
        }

        const string BaseUrlKey = "streams.baseUrl";

        /// <summary>
        /// WebGL: 게임을 띄운 페이지와 같은 도메인 (사이트에 올리므로 CORS 없음).
        /// 에디터·그 밖: PlayerPrefs "streams.baseUrl" 값, 없으면 로컬 wrangler dev (http://localhost:8787).
        /// </summary>
        public static string BaseUrl
        {
            get
            {
                if (Application.platform == RuntimePlatform.WebGLPlayer && !Application.isEditor)
                {
                    var page = new Uri(Application.absoluteURL);
                    return page.GetLeftPart(UriPartial.Authority);
                }
                var saved = PlayerPrefs.GetString(BaseUrlKey, "");
                return string.IsNullOrEmpty(saved) ? "http://localhost:8787" : saved.TrimEnd('/');
            }
            set
            {
                PlayerPrefs.SetString(BaseUrlKey, value ?? "");
                PlayerPrefs.Save();
            }
        }

        const string PlayerKey = "streams-player";

        /// <summary>기기에 저장한 익명 id (재방문 분석용). 웹의 localStorage['streams-player'] 와 같은 역할.</summary>
        public static string PlayerId
        {
            get
            {
                var id = PlayerPrefs.GetString(PlayerKey, "");
                if (string.IsNullOrEmpty(id))
                {
                    id = Guid.NewGuid().ToString();
                    PlayerPrefs.SetString(PlayerKey, id);
                    PlayerPrefs.Save();
                }
                return id;
            }
        }
    }
}
