// /api/streams 에 JSON POST. WebGL 에서도 도는 UnityWebRequest + Awaitable 만 쓴다 (스레드·Task.Run 없음).
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Streams.Net
{
    public sealed class ApiClient
    {
        readonly string baseUrl;
        const int TimeoutSeconds = 15;
        const int MaxRetries = 2;

        public ApiClient(string baseUrl) => this.baseUrl = baseUrl.TrimEnd('/');

        /// <summary>네트워크 오류·5xx 는 같은 본문으로 두 번까지 다시 보낸다 (docs/streams-api.md: 같은 토큰·같은 칸으로 재시도).</summary>
        public async Awaitable<TRes> PostAsync<TReq, TRes>(string path, TReq body)
        {
            var json = JsonUtility.ToJson(body);
            for (int attempt = 0; ; attempt++)
            {
                try
                {
                    return await SendOnce<TRes>(path, json);
                }
                catch (StreamsApiException e) when (e.Retryable && attempt < MaxRetries)
                {
                    Debug.LogWarning($"{path} 재시도 {attempt + 1}: {e.Message}");
                    await Awaitable.WaitForSecondsAsync(0.5f * (attempt + 1));
                }
            }
        }

        async Awaitable<TRes> SendOnce<TRes>(string path, string json)
        {
            using var req = new UnityWebRequest(baseUrl + "/api/streams" + path, UnityWebRequest.kHttpVerbPOST);
            req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json)) { contentType = "application/json" };
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("content-type", "application/json");
            req.SetRequestHeader("x-streams-client", ClientInfo.Header);
            req.timeout = TimeoutSeconds;

            await req.SendWebRequest();

            var text = req.downloadHandler?.text ?? "";
            if (req.result == UnityWebRequest.Result.Success)
                return JsonUtility.FromJson<TRes>(text);

            if (req.result == UnityWebRequest.Result.ConnectionError)
                throw new StreamsApiException(0, req.error);

            string error = null;
            try
            {
                error = JsonUtility.FromJson<ErrorResponse>(text)?.error;
            }
            catch (System.ArgumentException)
            {
                // JSON 이 아닌 오류 페이지
            }
            throw new StreamsApiException(req.responseCode, string.IsNullOrEmpty(error) ? req.error : error);
        }
    }
}
