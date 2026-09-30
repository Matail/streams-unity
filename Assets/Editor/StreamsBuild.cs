// 메뉴 STREAMS → Build WebGL. 배치 모드(CI)에서도:
//   Unity -batchmode -quit -projectPath . -executeMethod StreamsBuild.BuildWebGL
// 결과: Builds/WebGL/ → my-site 의 public/games/streams-unity/ 로 복사해 올린다 (README).
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class StreamsBuild
{
    const string ScenePath = "Assets/Scenes/Main.unity";
    const string OutDir = "Builds/WebGL";
    const long AssetLimitBytes = 25L * 1024 * 1024; // Cloudflare Workers 정적 파일 한 개 제한

    [MenuItem("STREAMS/Apply WebGL Settings")]
    public static void ApplyWebGLSettings()
    {
        // Brotli/Gzip 빌드는 서버가 Content-Encoding 을 붙여 줘야 한다 → 끄고 Cloudflare 전송 압축에 맡긴다
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
        PlayerSettings.WebGL.decompressionFallback = false;
        // 파일명에 해시 → 사이트에서 오래 캐시해도 새 빌드가 바로 반영됨
        PlayerSettings.WebGL.nameFilesAsHashes = true;
        PlayerSettings.WebGL.dataCaching = true;
        // 페이지 바탕을 my-site 와 같게 (Assets/WebGLTemplates/STREAMS)
        PlayerSettings.WebGL.template = "PROJECT:STREAMS";
        PlayerSettings.SetManagedStrippingLevel(NamedBuildTarget.WebGL, ManagedStrippingLevel.Medium);
        PlayerSettings.stripEngineCode = true;
        // 기본값(빌드 시간 우선)은 wasm 이 사이트 파일 한도(25 MiB)를 넘는다 → 크기 우선 + LTO
        UnityEditor.WebGL.UserBuildSettings.codeOptimization = UnityEditor.WebGL.WasmCodeOptimization.DiskSizeLTO;
        PlayerSettings.SetIl2CppCodeGeneration(NamedBuildTarget.WebGL, Il2CppCodeGeneration.OptimizeSize);
        PlayerSettings.runInBackground = true;
        if (string.IsNullOrEmpty(PlayerSettings.bundleVersion) || PlayerSettings.bundleVersion == "1.0")
            PlayerSettings.bundleVersion = "0.1.0"; // x-streams-client 헤더 버전
        EnsureScene();
        Debug.Log("STREAMS: WebGL 설정 적용");
    }

    [MenuItem("STREAMS/Build WebGL")]
    public static void BuildWebGL()
    {
        ApplyWebGLSettings();
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = new[] { ScenePath },
            locationPathName = OutDir,
            target = BuildTarget.WebGL,
            options = BuildOptions.None,
        });
        if (report.summary.result != BuildResult.Succeeded)
        {
            Debug.LogError($"STREAMS: WebGL 빌드 실패 ({report.summary.result})");
            if (Application.isBatchMode) EditorApplication.Exit(1);
            return;
        }

        var big = Directory.GetFiles(OutDir, "*", SearchOption.AllDirectories)
            .Select(f => new FileInfo(f)).Where(f => f.Length > AssetLimitBytes).ToList();
        foreach (var f in big)
            Debug.LogWarning($"STREAMS: {f.Name} {f.Length / 1048576f:F1} MiB — 사이트 정적 파일 한도(25 MiB) 초과. R2 로 옮기거나 줄여야 함");
        Debug.Log($"STREAMS: WebGL 빌드 완료 → {OutDir} ({report.summary.totalSize / 1048576f:F1} MiB)");
        if (Application.isBatchMode && big.Count > 0) EditorApplication.Exit(2);
    }

    /// <summary>화면은 코드가 만든다(GameBootstrap). 씬은 빈 씬 하나면 된다.</summary>
    static void EnsureScene()
    {
        if (!File.Exists(ScenePath))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            var scene = EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects, NewSceneMode.Single);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }
        if (!EditorBuildSettings.scenes.Any(s => s.path == ScenePath))
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
    }
}
